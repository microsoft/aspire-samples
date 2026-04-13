import { useState, useEffect } from 'react';

interface PageStats {
  [page: string]: number;
}

interface StatsResponse {
  totalPages: number;
  stats: PageStats;
}

const DEFAULT_PAGES = ['home', 'about', 'contact', 'products', 'services', 'blog'];

function App() {
  const [stats, setStats] = useState<PageStats>({});
  const [loading, setLoading] = useState(false);

  const fetchStats = async () => {
    try {
      const response = await fetch('/api/stats');
      const data: StatsResponse = await response.json();
      setStats(data.stats);
    } catch (error) {
      console.error('Failed to fetch stats:', error);
    }
  };

  useEffect(() => {
    fetchStats();
    const interval = setInterval(fetchStats, 5000);
    return () => clearInterval(interval);
  }, []);

  const handleCardClick = async (page: string) => {
    setLoading(true);
    try {
      const response = await fetch(`/api/visit/${encodeURIComponent(page)}`, {
        method: 'POST',
      });
      const data = await response.json();

      // Update just this page's count in place
      setStats(prev => ({
        ...prev,
        [page]: data.visits
      }));
    } catch (error) {
      console.error('Failed to record visit:', error);
    } finally {
      setLoading(false);
    }
  };

  const resetStats = async () => {
    if (!confirm('Are you sure you want to reset all statistics?')) return;

    setLoading(true);
    try {
      await fetch('/api/stats', { method: 'DELETE' });
      await fetchStats();
    } catch (error) {
      console.error('Failed to reset stats:', error);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="container">
      <h1>Visit Counter</h1>
      <p className="subtitle">Click any page below to record a visit</p>

      <div className="stats-section">
        <div className="stats-header">
          <button
            className="reset-button"
            onClick={resetStats}
            disabled={loading}
          >
            Reset All
          </button>
        </div>

        <div className="stats-grid">
          {DEFAULT_PAGES.map((page) => {
            const count = stats[page] || 0;
            return (
              <button
                key={page}
                className="stat-card"
                onClick={() => handleCardClick(page)}
                disabled={loading}
                title={`Click to visit ${page}`}
              >
                <div className="stat-page">{page}</div>
                <div className="stat-count">{count.toLocaleString()}</div>
                <div className="stat-label">visits • click to visit</div>
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
}

export default App;
