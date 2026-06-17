import express from 'express';
import { createClient } from 'redis';

const app = express();
const port = process.env.PORT || 3000;
const pages = ['home', 'about', 'contact', 'products', 'services', 'blog'];
const allowedPages = new Set(pages);
const visitKeyPrefix = 'sample:node-express-redis:visits:';

function getVisitKey(page) {
  return `${visitKeyPrefix}${page}`;
}

const visitKeys = pages.map((page) => getVisitKey(page));

app.use(express.json({ limit: '10kb' }));

// Initialize Redis client using Aspire connection properties
// Aspire provides REDIS_URI for non-.NET apps
const redisUrl = process.env.REDIS_URI;
const redis = createClient({ url: redisUrl });

redis.on('error', (err) => console.error('Redis error:', err));
redis.on('connect', () => console.log('✓ Connected to Redis'));

// Connect to Redis
await redis.connect();

function validatePage(req, res, next) {
  const { page } = req.params;

  if (!allowedPages.has(page)) {
    return res.status(400).json({
      error: 'Unsupported page',
      allowedPages: pages,
    });
  }

  next();
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
app.post('/visit/:page', validatePage, async (req, res) => {
  const { page } = req.params;
  const count = await redis.incr(getVisitKey(page));

  res.json({
    page,
    visits: count,
    message: `Visit recorded for ${page}`,
  });
});

// Get visit count for a page
app.get('/visit/:page', validatePage, async (req, res) => {
  const { page } = req.params;
  const count = await redis.get(getVisitKey(page));

  res.json({
    page,
    visits: count ? parseInt(count, 10) : 0,
  });
});

// Get stats for all pages
app.get('/stats', async (req, res) => {
  const counts = await redis.mGet(visitKeys);
  const stats = {};

  pages.forEach((page, index) => {
    const count = counts[index];
    stats[page] = count ? parseInt(count, 10) : 0;
  });

  res.json({
    totalPages: pages.length,
    stats,
  });
});

// Reset all stats
app.delete('/stats', async (req, res) => {
  const pagesCleared = await redis.del(...visitKeys);

  res.json({
    message: 'All stats reset',
    pagesCleared,
  });
});

app.use((err, req, res, next) => {
  if (err?.type === 'entity.too.large') {
    return res.status(413).json({ error: 'Request body too large' });
  }

  next(err);
});

app.listen(port, () => {
  console.log(`✓ API listening on port ${port}`);
});
