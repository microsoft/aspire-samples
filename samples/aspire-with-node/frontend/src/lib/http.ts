/** Small fetch helper shared by the data hooks. */

/**
 * Fetch that retries once on a network error or 5xx response, with a short backoff,
 * to ride out transient upstream blips.
 */
export async function fetchWithRetry(url: string, retries = 1): Promise<Response> {
  let lastError: unknown;
  for (let attempt = 0; attempt <= retries; attempt++) {
    try {
      const res = await fetch(url);
      if (res.ok || res.status < 500 || attempt === retries) return res;
    } catch (err) {
      lastError = err;
      if (attempt === retries) throw err;
    }
    await new Promise((resolve) => setTimeout(resolve, 500 * (attempt + 1)));
  }
  throw lastError;
}
