/**
 * The task board: an in-memory view of recent tasks. The API adds a task when it publishes it, and
 * updates it from the `task_status` and `results` messages the workers send back. The UI follows
 * it over server-sent events (`GET /tasks/stream`).
 *
 * The board is capped at MAX_TASKS (oldest first out) and is per API instance; a real system would
 * keep task state in a database.
 */
import { tasksPending, taskTurnaround } from './telemetry.js';

const MAX_TASKS = 500;
const SNAPSHOT_SIZE = 50;

export const STATUSES = /** @type {const} */ (['queued', 'processing', 'completed', 'failed']);

/**
 * @typedef {{ headline: string, facts: { label: string, value: string }[], details: Record<string, unknown> }} TaskResult
 * @typedef {{
 *   id: string, type: string, name: string, data: string, status: typeof STATUSES[number],
 *   worker: string, createdAt: string, startedAt?: string, completedAt?: string, durationMs?: number,
 *   result?: TaskResult, error?: { type: string, message: string }, traceId?: string
 * }} Task
 */

/** @type {Map<string, Task>} */
const tasks = new Map();
/** @type {Set<(snapshot: ReturnType<typeof snapshot>) => void>} */
const subscribers = new Set();
let notifyPending = false;

const isPending = (/** @type {Task} */ task) => task.status === 'queued' || task.status === 'processing';
const typeAttributes = (/** @type {Task} */ task) => ({ 'taskqueue.task.type': task.type });

/** Coalesces bursts of updates into one push per tick. */
function changed() {
  if (notifyPending || subscribers.size === 0) return;
  notifyPending = true;
  setTimeout(() => {
    notifyPending = false;
    const current = snapshot();
    for (const send of subscribers) send(current);
  }, 100);
}

/** @param {Task} task */
function forget(task) {
  tasks.delete(task.id);
  if (isPending(task)) tasksPending.add(-1, typeAttributes(task));
}

/** @param {Task} task */
export function add(task) {
  tasks.set(task.id, task);
  tasksPending.add(1, typeAttributes(task));
  for (const oldest of tasks.values()) {
    if (tasks.size <= MAX_TASKS) break;
    forget(oldest);
  }
  changed();
}

/** Removes a task whose message never reached the broker. @param {string} taskId */
export function remove(taskId) {
  const task = tasks.get(taskId);
  if (task) forget(task);
  changed();
}

/** @param {string} taskId */
export const get = (taskId) => tasks.get(taskId);

/** Newest first. @param {number} limit */
export const list = (limit) => [...tasks.values()].reverse().slice(0, limit);

export function clear() {
  const count = tasks.size;
  for (const task of [...tasks.values()]) forget(task);
  changed();
  return count;
}

/** @param {{ taskId: string, worker: string, startedAt: string }} update */
export function markProcessing({ taskId, worker, startedAt }) {
  const task = tasks.get(taskId);
  if (!task || task.status !== 'queued') return task;
  Object.assign(task, { status: 'processing', worker, startedAt });
  changed();
  return task;
}

/**
 * Records a worker's outcome, plus the task's turnaround (submitted -> result) metric.
 * @param {{ taskId: string, status: 'completed' | 'failed', worker: string, completedAt: string, durationMs: number, result?: TaskResult, error?: Task['error'] }} update
 */
export function markFinished({ taskId, status, worker, completedAt, durationMs, result, error }) {
  const task = tasks.get(taskId);
  if (!task) return undefined; // cleared from the board while it was in flight
  const wasPending = isPending(task);
  Object.assign(task, { status, worker, completedAt, durationMs, result, error });
  if (wasPending) {
    tasksPending.add(-1, typeAttributes(task));
    const turnaround = (Date.parse(completedAt) - Date.parse(task.createdAt)) / 1000;
    if (turnaround >= 0) taskTurnaround.record(turnaround, { ...typeAttributes(task), 'taskqueue.task.outcome': status });
  }
  changed();
  return task;
}

/** The latest tasks plus totals by status and per task type (one type per worker), as pushed to the UI. */
export function snapshot() {
  const totals = Object.fromEntries(STATUSES.map((status) => [status, 0]));
  /** @type {Record<string, Record<string, number> & { durationMs: number }>} */
  const byType = {};
  for (const task of tasks.values()) {
    totals[task.status] += 1;
    const stats = (byType[task.type] ??= { ...Object.fromEntries(STATUSES.map((status) => [status, 0])), durationMs: 0 });
    stats[task.status] += 1;
    if (task.status === 'completed') stats.durationMs += task.durationMs ?? 0;
  }
  const workers = Object.fromEntries(
    Object.entries(byType).map(([type, { durationMs, ...counts }]) => [
      type,
      { ...counts, averageDurationMs: counts.completed ? Math.round(durationMs / counts.completed) : null },
    ]),
  );
  return { tasks: list(SNAPSHOT_SIZE), totals: { ...totals, all: tasks.size }, workers };
}

/** @param {(snapshot: ReturnType<typeof snapshot>) => void} send @returns {() => void} unsubscribe */
export function subscribe(send) {
  subscribers.add(send);
  return () => subscribers.delete(send);
}
