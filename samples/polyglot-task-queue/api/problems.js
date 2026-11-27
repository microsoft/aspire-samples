/**
 * RFC 9457 problem details. Handlers throw a `ProblemError`; the Express error handler in
 * index.js writes it as `application/problem+json` with the request's trace id, so a failed
 * call can be pasted straight into the Aspire dashboard's trace search.
 */
import { trace } from '@opentelemetry/api';

const PROBLEM_TYPES = {
  400: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  404: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
  413: 'https://tools.ietf.org/html/rfc9110#section-15.5.14',
  500: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
  503: 'https://tools.ietf.org/html/rfc9110#section-15.6.4',
};

export class ProblemError extends Error {
  /**
   * @param {number} status
   * @param {string} title
   * @param {string} detail
   * @param {Record<string, unknown>} [extensions] extra members, such as `errors`
   */
  constructor(status, title, detail, extensions = {}) {
    super(detail);
    this.status = status;
    this.title = title;
    this.extensions = extensions;
  }
}

/** @param {Record<string, string[]>} errors validation messages keyed by field */
export const validationProblem = (errors) =>
  new ProblemError(400, 'One or more validation errors occurred.', 'See errors for the invalid fields.', { errors });

export const brokerUnavailable = () =>
  new ProblemError(503, 'Message broker unavailable', 'RabbitMQ is not reachable, so the task could not be queued. Try again shortly.');

/**
 * @param {import('express').Request} req
 * @param {import('express').Response} res
 * @param {ProblemError} problem
 */
export function writeProblem(req, res, problem) {
  res
    .status(problem.status)
    .type('application/problem+json')
    .json({
      type: PROBLEM_TYPES[problem.status] ?? 'about:blank',
      title: problem.title,
      status: problem.status,
      detail: problem.message,
      instance: req.originalUrl,
      traceId: trace.getActiveSpan()?.spanContext().traceId,
      ...problem.extensions,
    });
}
