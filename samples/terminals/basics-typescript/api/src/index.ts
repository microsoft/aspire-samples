import { createClient } from 'redis';
import { createApp } from './app.ts';
import { shutdownTelemetry } from './instrumentation.ts';
import { logger } from './logger.ts';

const url = process.env.REDIS_URI;
const port = Number(process.env.PORT);
if (!url || !Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error('Aspire must supply REDIS_URI and a valid PORT.');
}

const redis = createClient({
    url,
    disableOfflineQueue: true,
    socket: { connectTimeout: 5000 },
});
redis.on('error', error => logger.error({ err: error }, 'Redis connection error'));
await redis.connect();
// The AppHost enables the seed endpoint (and its dashboard command) in run mode only.
const demoEndpoints = process.env.ENABLE_DEMO_ENDPOINTS === 'true';
const server = createApp(redis, { demoEndpoints }).listen(port, '0.0.0.0', () => {
    logger.info({ port, demoEndpoints }, `Notes API listening on port ${port}`);
});

let stopping = false;
async function stop() {
    if (stopping) return;
    stopping = true;
    try {
        try {
            await new Promise<void>((resolve, reject) => {
                server.close(error => error ? reject(error) : resolve());
            });
        } finally {
            redis.destroy();
            await shutdownTelemetry();
        }
    } catch (error) {
        logger.error({ err: error }, 'Shutdown failed');
        process.exitCode = 1;
    }
}
process.on('SIGTERM', () => { void stop(); });
process.on('SIGINT', () => { void stop(); });
