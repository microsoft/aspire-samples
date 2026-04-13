import express from 'express';
import { createClient } from 'redis';

const app = express();
const port = process.env.PORT || 3000;

app.use(express.json());

// Initialize Redis client using Aspire connection properties
// Aspire provides REDIS_URI for non-.NET apps
const redisUrl = process.env.REDIS_URI;
const redis = createClient({ url: redisUrl });

redis.on('error', (err) => console.error('Redis error:', err));
redis.on('connect', () => console.log('✓ Connected to Redis'));

// Connect to Redis
await redis.connect();

async function getVisitKeys() {
  const keys = [];
  for await (const key of redis.scanIterator({ MATCH: 'visits:*', COUNT: 100 })) {
    keys.push(key);
  }

  return keys;
}

// Root endpoint
app.get('/', (req, res) => {
  res.json({
    message: 'Visit Counter API',
    endpoints: ['/visit/:page', '/stats', '/health'],
  });
});

// Health check
app.get('/health', async (req, res) => {
  try {
    await redis.ping();
    res.json({ status: 'healthy', redis: 'connected' });
  } catch (error) {
    res.status(503).json({ status: 'unhealthy', redis: 'disconnected' });
  }
});

// Track a visit to a page
app.post('/visit/:page', async (req, res) => {
  const { page } = req.params;
  const count = await redis.incr(`visits:${page}`);

  res.json({
    page,
    visits: count,
    message: `Visit recorded for ${page}`,
  });
});

// Get visit count for a page
app.get('/visit/:page', async (req, res) => {
  const { page } = req.params;
  const count = await redis.get(`visits:${page}`);

  res.json({
    page,
    visits: count ? parseInt(count) : 0,
  });
});

// Get stats for all pages
app.get('/stats', async (req, res) => {
  const keys = await getVisitKeys();
  const counts = keys.length > 0 ? await redis.mGet(keys) : [];
  const stats = {};

  keys.forEach((key, index) => {
    const page = key.replace('visits:', '');
    const count = counts[index];
    stats[page] = count ? parseInt(count, 10) : 0;
  });

  res.json({
    totalPages: keys.length,
    stats,
  });
});

// Reset all stats
app.delete('/stats', async (req, res) => {
  const keys = await getVisitKeys();
  if (keys.length > 0) {
    await redis.del(...keys);
  }

  res.json({
    message: 'All stats reset',
    pagesCleared: keys.length,
  });
});

app.listen(port, () => {
  console.log(`✓ API listening on port ${port}`);
});
