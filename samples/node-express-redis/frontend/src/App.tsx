import { useEffect, useRef, useState } from 'react';
import type { ComponentType } from 'react';
import {
  IconHome2,
  IconInfoCircle,
  IconMail,
  IconShoppingBag,
  IconTool,
  IconArticle,
  IconChartBar,
  IconRefresh,
} from '@tabler/icons-react';
import styles from './App.module.css';

interface PageStats {
  [page: string]: number;
}

interface StatsResponse {
  totalPages: number;
  stats: PageStats;
}

interface VisitResponse {
  page: string;
  visits: number;
  message: string;
}

type IconType = ComponentType<{ size?: number; stroke?: number }>;

interface PageMeta {
  label: string;
  Icon: IconType;
  colorVar: string;
}

const PAGE_META: Record<string, PageMeta> = {
  home: { label: 'Home', Icon: IconHome2, colorVar: '--c-coral' },
  about: { label: 'About', Icon: IconInfoCircle, colorVar: '--c-sky' },
  contact: { label: 'Contact', Icon: IconMail, colorVar: '--c-green' },
  products: { label: 'Products', Icon: IconShoppingBag, colorVar: '--c-pink' },
  services: { label: 'Services', Icon: IconTool, colorVar: '--c-orange' },
  blog: { label: 'Blog', Icon: IconArticle, colorVar: '--c-violet' },
};

const DEFAULT_PAGES = Object.keys(PAGE_META);

function App() {
  const [stats, setStats] = useState<PageStats>({});
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState('');
  const busyRef = useRef(false);

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
    if (busyRef.current) return;
    busyRef.current = true;
    setLoading(true);
    const label = PAGE_META[page]?.label ?? page;
    try {
      const response = await fetch(`/api/visit/${encodeURIComponent(page)}`, {
        method: 'POST',
      });
      const data: VisitResponse = await response.json();
      setStats((prev) => ({ ...prev, [page]: data.visits }));
      setMessage(`Recorded a visit to ${label}. Now ${data.visits.toLocaleString()} visits.`);
    } catch (error) {
      console.error('Failed to record visit:', error);
      setMessage(`Could not record a visit to ${label}. Please try again.`);
    } finally {
      busyRef.current = false;
      setLoading(false);
    }
  };

  const resetStats = async () => {
    if (busyRef.current) return;
    if (!confirm('Are you sure you want to reset all statistics?')) return;
    busyRef.current = true;
    setLoading(true);
    try {
      await fetch('/api/stats', { method: 'DELETE' });
      await fetchStats();
      setMessage('All visit counts were reset to zero.');
    } catch (error) {
      console.error('Failed to reset stats:', error);
      setMessage('Could not reset the statistics. Please try again.');
    } finally {
      busyRef.current = false;
      setLoading(false);
    }
  };

  const total = DEFAULT_PAGES.reduce((sum, page) => sum + (stats[page] || 0), 0);

  return (
    <>
      <a className="skip-link" href="#main">
        Skip to dashboard
      </a>

      <div className={styles.page}>
        <header className={styles.header}>
          <div className={styles.shapes} aria-hidden="true">
            <svg className={`${styles.shape} ${styles.shapeTri}`} width="46" height="46" viewBox="0 0 46 46">
              <polygon points="23,2 44,44 2,44" fill="var(--c-green)" stroke="var(--ink)" strokeWidth="3" strokeLinejoin="round" />
            </svg>
            <svg className={`${styles.shape} ${styles.shapeZig}`} width="64" height="22" viewBox="0 0 64 22">
              <polyline points="2,18 12,4 22,18 32,4 42,18 52,4 62,18" fill="none" stroke="var(--c-blue)" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
            <svg className={`${styles.shape} ${styles.shapeDots}`} width="58" height="22" viewBox="0 0 58 22">
              <g fill="var(--c-pink)" stroke="var(--ink)" strokeWidth="2">
                <circle cx="9" cy="11" r="7" />
                <circle cx="29" cy="11" r="7" />
                <circle cx="49" cy="11" r="7" />
              </g>
            </svg>
          </div>

          <p className={styles.kicker}>Aspire · Redis dashboard</p>
          <h1 className={styles.title}>
            Visit <span className={styles.titleAccent}>Counter</span>
          </h1>
          <p className={styles.subtitle}>
            A page-visit counter backed by Redis on Aspire. Tap a page to record a visit and watch the
            totals update live.
          </p>
        </header>

        <section className={styles.hero} aria-label="Total visits recorded">
          <div className={styles.heroMain}>
            <span className={styles.heroLabel}>Total visits recorded</span>
            <span className={styles.heroNumber}>{total.toLocaleString()}</span>
          </div>
          <div className={styles.heroAside}>
            <span className={styles.livePill}>
              <span className={styles.liveDot} aria-hidden="true" />
              Live
            </span>
            <span className={styles.heroIcon} aria-hidden="true">
              <IconChartBar size={36} stroke={2.4} />
            </span>
          </div>
        </section>

        <main id="main" className={styles.section}>
          <div className={styles.sectionHead}>
            <h2 className={styles.sectionTitle}>Pages</h2>
            <button className={styles.reset} onClick={resetStats} disabled={loading} type="button">
              <IconRefresh size={20} stroke={2.2} aria-hidden="true" />
              Reset all
            </button>
          </div>

          <div className={styles.grid}>
            {DEFAULT_PAGES.map((page) => {
              const { label, Icon, colorVar } = PAGE_META[page];
              const count = stats[page] || 0;
              return (
                <button
                  key={page}
                  type="button"
                  className={styles.card}
                  style={{ ['--card' as string]: `var(${colorVar})` }}
                  onClick={() => handleCardClick(page)}
                  disabled={loading}
                  aria-label={`Record a visit to ${label}. Currently ${count.toLocaleString()} visits.`}
                >
                  <span className={styles.cardIcon} aria-hidden="true">
                    <Icon size={24} stroke={2.2} />
                  </span>
                  <span className={styles.cardName}>{label}</span>
                  <span className={styles.cardCount}>{count.toLocaleString()}</span>
                  <span className={styles.cardHint}>visits · tap to add</span>
                </button>
              );
            })}
          </div>
        </main>

        <p role="status" aria-live="polite" className="visually-hidden">
          {message}
        </p>
      </div>
    </>
  );
}

export default App;
