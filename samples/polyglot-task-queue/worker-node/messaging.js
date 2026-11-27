/**
 * RabbitMQ plumbing for the Node.js worker (the same helpers as the API's messaging.js). Every
 * publish and every consumed message gets a span with the OpenTelemetry messaging
 * semantic-convention attributes, and the W3C trace context travels in the message headers, so
 * the task's trace continues from the API into this worker and back.
 */
import { randomUUID } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import { context, propagation, SpanKind, SpanStatusCode } from '@opentelemetry/api';
import amqp from 'amqplib';
import { logger } from './logger.js';
import { tracer } from './telemetry.js';

export const STATUS_QUEUE = 'task_status';
export const RESULTS_QUEUE = 'results';

const brokerUrl = new URL(process.env.MESSAGING_URI ?? 'amqp://localhost:5672');
const brokerAttributes = {
  'messaging.system': 'rabbitmq',
  'server.address': brokerUrl.hostname,
  'server.port': Number(brokerUrl.port || 5672),
};

/** @type {{ connection: import('amqplib').ChannelModel, publisher: import('amqplib').ConfirmChannel } | null} */
let state = null;

export const isConnected = () => state !== null;

/** AMQP header values arrive as strings or Buffers, depending on the publisher's client library. */
const headerGetter = {
  /** @param {Record<string, unknown>} carrier */
  keys: (carrier) => Object.keys(carrier),
  /** @param {Record<string, unknown>} carrier @param {string} key */
  get: (carrier, key) => {
    const value = carrier[key];
    return Buffer.isBuffer(value) ? value.toString('utf8') : /** @type {string | undefined} */ (value);
  },
};

/**
 * Messages go through the default exchange with the queue name as the routing key, so the
 * destination is the routing key alone (see the RabbitMQ semantic conventions).
 * @param {'send' | 'process'} operation
 * @param {string} queue
 * @param {{ messageId?: string, correlationId?: string, size: number }} message
 */
const messagingAttributes = (operation, queue, { messageId, correlationId, size }) => ({
  ...brokerAttributes,
  'messaging.operation.type': operation,
  'messaging.operation.name': operation === 'send' ? 'publish' : 'process',
  'messaging.destination.name': queue,
  'messaging.rabbitmq.destination.routing_key': queue,
  ...(messageId && { 'messaging.message.id': messageId }),
  ...(correlationId && { 'messaging.message.conversation_id': correlationId }),
  'messaging.message.body.size': size,
});

/**
 * Connects to RabbitMQ (retrying until it is reachable), declares the queues, and calls
 * `onConnected` with a consumer channel. Reconnects the same way if the connection drops.
 * @param {string[]} queues
 * @param {number} prefetch how many unacknowledged messages this process handles at once
 * @param {(channel: import('amqplib').Channel) => Promise<void>} onConnected
 */
export async function connect(queues, prefetch, onConnected) {
  for (let attempt = 1; ; attempt += 1) {
    /** @type {import('amqplib').ChannelModel | undefined} */
    let connection;
    try {
      connection = await amqp.connect(brokerUrl.href);
      const publisher = await connection.createConfirmChannel();
      const consumer = await connection.createChannel();
      for (const queue of queues) await publisher.assertQueue(queue, { durable: true });
      await consumer.prefetch(prefetch);
      // Publishing must work before the first delivery arrives.
      state = { connection, publisher };
      await onConnected(consumer);

      connection.on('error', (err) => logger.warn({ error: err.message }, 'RabbitMQ connection error'));
      connection.on('close', () => {
        state = null;
        logger.warn('RabbitMQ connection closed; reconnecting');
        void connect(queues, prefetch, onConnected);
      });
      logger.info({ broker: brokerUrl.host, queues }, `Connected to RabbitMQ at ${brokerUrl.host}`);
      return;
    } catch (err) {
      state = null;
      await connection?.close().catch(() => {});
      const error = /** @type {Error & { code?: string }} */ (err);
      logger.warn({ attempt, error: error.message || error.code || error.name }, 'RabbitMQ is not reachable yet; retrying');
      await delay(Math.min(attempt * 1000, 5000));
    }
  }
}

/**
 * Publishes a JSON message under a PRODUCER span and waits for the broker's confirm. The span's
 * context is injected into the message headers, so the consumer's span becomes its child.
 * @param {string} queue
 * @param {{ taskId: string } & Record<string, unknown>} message
 * @param {{ attributes?: import('@opentelemetry/api').Attributes, links?: import('@opentelemetry/api').Link[] }} [options]
 *   extra span attributes, and links for a span that starts a new trace
 * @returns {Promise<string>} the trace id the message carries
 */
export function publish(queue, message, { attributes, links } = {}) {
  const body = Buffer.from(JSON.stringify(message));
  const messageId = randomUUID();
  const spanAttributes = {
    ...messagingAttributes('send', queue, { messageId, correlationId: message.taskId, size: body.length }),
    ...attributes,
  };

  return tracer.startActiveSpan(`publish ${queue}`, { kind: SpanKind.PRODUCER, attributes: spanAttributes, links }, async (span) => {
    try {
      if (!state) throw Object.assign(new Error('RabbitMQ is not connected'), { name: 'BrokerUnavailableError' });
      const { publisher } = state;

      /** @type {Record<string, string>} */
      const headers = {};
      propagation.inject(context.active(), headers);

      await new Promise((resolve, reject) => {
        publisher.sendToQueue(
          queue,
          body,
          {
            persistent: true,
            contentType: 'application/json',
            messageId,
            correlationId: message.taskId,
            timestamp: Math.floor(Date.now() / 1000),
            headers,
          },
          (err) => (err ? reject(err) : resolve(undefined)),
        );
      });
      return span.spanContext().traceId;
    } catch (err) {
      const error = /** @type {Error} */ (err);
      span.recordException(error);
      span.setStatus({ code: SpanStatusCode.ERROR, message: error.message });
      span.setAttribute('error.type', error.name);
      throw err;
    } finally {
      span.end();
    }
  });
}

/**
 * Consumes JSON messages from a queue. Each message is handled under a CONSUMER span whose parent
 * is the producer span from the message headers, then acknowledged. Messages that can't be
 * handled are dropped (nack without requeue) rather than redelivered forever.
 * @param {import('amqplib').Channel} channel
 * @param {string} queue
 * @param {(payload: any, span: import('@opentelemetry/api').Span) => void | Promise<void>} handler
 */
export async function consume(channel, queue, handler) {
  await channel.consume(queue, (msg) => {
    if (!msg) return; // the broker cancelled the consumer
    const parent = propagation.extract(context.active(), msg.properties.headers ?? {}, headerGetter);
    const attributes = {
      ...messagingAttributes('process', queue, {
        messageId: msg.properties.messageId,
        correlationId: msg.properties.correlationId,
        size: msg.content.length,
      }),
      'messaging.rabbitmq.message.delivery_tag': msg.fields.deliveryTag,
    };

    void tracer.startActiveSpan(`process ${queue}`, { kind: SpanKind.CONSUMER, attributes }, parent, async (span) => {
      try {
        await handler(JSON.parse(msg.content.toString('utf8')), span);
        channel.ack(msg);
      } catch (err) {
        const error = /** @type {Error} */ (err);
        span.recordException(error);
        span.setStatus({ code: SpanStatusCode.ERROR, message: error.message });
        span.setAttribute('error.type', error.name);
        logger.error({ queue, error: error.message }, `Dropped a ${queue} message that could not be processed`);
        channel.nack(msg, false, false);
      } finally {
        span.end();
      }
    });
  });
}
