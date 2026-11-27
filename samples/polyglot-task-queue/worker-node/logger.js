/**
 * Structured logger. pino writes JSON to stdout (the dashboard's console logs); when
 * instrumentation.js is preloaded, the pino instrumentation also sends every record to the
 * dashboard's Structured logs with its fields as attributes and the active trace/span ids.
 */
import { pino } from 'pino';

export const logger = pino({ level: process.env.LOG_LEVEL ?? 'info' });
