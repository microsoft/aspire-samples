/** Debounced type-ahead city search with keyboard navigation and outside-click close. */
import { useCallback, useEffect, useRef, useState } from 'react';
import type { KeyboardEvent, FormEvent } from 'react';
import type { GeoResult } from '../types/weather';

interface UseGeocodeSearchOptions {
  /** Called with the chosen result's coordinates (App flies the map there). */
  onSelect: (lat: number, lon: number) => void;
  /** Dismisses a stale control error as the user resumes typing. */
  onClearError?: () => void;
}

export function useGeocodeSearch({ onSelect, onClearError }: UseGeocodeSearchOptions) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<GeoResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const [searchOpen, setSearchOpen] = useState(false);

  const debounceRef = useRef<number | undefined>(undefined);
  const requestRef = useRef(0);
  const formRef = useRef<HTMLFormElement>(null);

  // Debounced type-ahead geocoding; stale responses are ignored (no aborted-request noise).
  const runGeocode = useCallback(async (term: string) => {
    const requestId = ++requestRef.current;
    setSearching(true);
    try {
      const res = await fetch(`/api/geocode?q=${encodeURIComponent(term)}`);
      if (requestRef.current !== requestId) return;
      if (!res.ok) {
        const body = (await res.json().catch(() => null)) as { error?: string } | null;
        throw new Error(body?.error ?? `HTTP ${res.status}`);
      }
      const data: { results: GeoResult[] } = await res.json();
      if (requestRef.current !== requestId) return;
      setResults(data.results);
      setActiveIndex(-1);
      setSearchOpen(true);
    } catch (err) {
      if (requestRef.current !== requestId) return;
      setResults([]);
      console.error('[geocode] request failed:', err);
    } finally {
      if (requestRef.current === requestId) setSearching(false);
    }
  }, []);

  const selectResult = useCallback(
    (result: GeoResult) => {
      setResults([]);
      setSearchOpen(false);
      setActiveIndex(-1);
      setQuery(result.name);
      onSelect(result.latitude, result.longitude);
    },
    [onSelect],
  );

  const handleQueryChange = useCallback(
    (value: string) => {
      setQuery(value);
      onClearError?.();
      window.clearTimeout(debounceRef.current);
      const term = value.trim();
      if (term.length < 2) {
        requestRef.current++;
        setResults([]);
        setSearchOpen(false);
        setActiveIndex(-1);
        setSearching(false);
        return;
      }
      debounceRef.current = window.setTimeout(() => runGeocode(term), 250);
    },
    [runGeocode, onClearError],
  );

  const handleKeyDown = useCallback(
    (event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'ArrowDown') {
        if (results.length === 0) return;
        event.preventDefault();
        setSearchOpen(true);
        setActiveIndex((index) => (index + 1) % results.length);
      } else if (event.key === 'ArrowUp') {
        if (results.length === 0) return;
        event.preventDefault();
        setSearchOpen(true);
        setActiveIndex((index) => (index <= 0 ? results.length - 1 : index - 1));
      } else if (event.key === 'Enter') {
        if (searchOpen && results.length > 0) {
          event.preventDefault();
          selectResult(results[activeIndex >= 0 ? activeIndex : 0]);
        }
      } else if (event.key === 'Escape') {
        setSearchOpen(false);
        setActiveIndex(-1);
      }
    },
    [results, searchOpen, activeIndex, selectResult],
  );

  const handleSubmit = useCallback(
    (event: FormEvent) => {
      event.preventDefault();
      window.clearTimeout(debounceRef.current);
      const term = query.trim();
      if (term.length < 2) return;
      if (results.length > 0) {
        selectResult(results[activeIndex >= 0 ? activeIndex : 0]);
      } else {
        runGeocode(term);
      }
    },
    [query, results, activeIndex, selectResult, runGeocode],
  );

  // Close the suggestions when clicking outside the search box.
  useEffect(() => {
    if (!searchOpen) return;
    const handlePointerDown = (event: PointerEvent) => {
      if (formRef.current && !formRef.current.contains(event.target as Node)) {
        setSearchOpen(false);
      }
    };
    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [searchOpen]);

  return {
    query,
    results,
    searching,
    activeIndex,
    searchOpen,
    formRef,
    setActiveIndex,
    setSearchOpen,
    handleQueryChange,
    handleKeyDown,
    handleSubmit,
    selectResult,
  };
}

/** The bundle returned by {@link useGeocodeSearch}, consumed by the search UI. */
export type GeocodeSearch = ReturnType<typeof useGeocodeSearch>;
