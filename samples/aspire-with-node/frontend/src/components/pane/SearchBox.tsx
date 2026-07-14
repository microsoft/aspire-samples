/** City search input with type-ahead suggestions and a "use my location" button. */
import type { GeocodeSearch } from '../../hooks/useGeocodeSearch';
import { LocateGlyph, SearchGlyph } from '../icons/Glyphs';

/** Wraps the matched portion of a suggestion so it stands out as you type. */
function highlightMatch(text: string, query: string) {
  const term = query.trim();
  const index = term ? text.toLowerCase().indexOf(term.toLowerCase()) : -1;
  if (index === -1) return text;
  return (
    <>
      {text.slice(0, index)}
      <mark className="sr-match">{text.slice(index, index + term.length)}</mark>
      {text.slice(index + term.length)}
    </>
  );
}

interface SearchBoxProps {
  search: GeocodeSearch;
  onLocate: () => void;
}

export function SearchBox({ search, onLocate }: SearchBoxProps) {
  const {
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
  } = search;

  return (
    <form className="search" role="search" onSubmit={handleSubmit} ref={formRef}>
      <input
        type="search"
        role="combobox"
        className="search-input"
        placeholder="Search for a city…"
        value={query}
        onChange={(event) => handleQueryChange(event.target.value)}
        onKeyDown={handleKeyDown}
        onFocus={() => {
          if (results.length > 0) setSearchOpen(true);
        }}
        aria-label="Search for a city"
        aria-autocomplete="list"
        aria-expanded={searchOpen && results.length > 0}
        aria-controls="search-suggestions"
        aria-activedescendant={activeIndex >= 0 ? `search-option-${activeIndex}` : undefined}
        autoComplete="off"
      />
      <button type="submit" className="control-btn" aria-label="Search" disabled={searching}>
        {searching ? <span className="spinner" aria-hidden="true" /> : <SearchGlyph />}
      </button>
      <button
        type="button"
        className="control-btn"
        onClick={onLocate}
        aria-label="Use my location"
        title="Use my location"
      >
        <LocateGlyph />
      </button>
      {searchOpen && (results.length > 0 || (query.trim().length >= 2 && !searching)) && (
        <ul className="search-results" role="listbox" id="search-suggestions" aria-label="City suggestions">
          {results.length === 0 ? (
            <li className="search-status">No matches</li>
          ) : (
            results.map((result, index) => (
              <li
                key={`${result.latitude},${result.longitude},${index}`}
                id={`search-option-${index}`}
                role="option"
                aria-selected={index === activeIndex}
                className={`search-result ${index === activeIndex ? 'active' : ''}`}
                onClick={() => selectResult(result)}
                onMouseMove={() => setActiveIndex(index)}
              >
                <span className="sr-name">{highlightMatch(result.name, query)}</span>
                <span className="sr-meta">
                  {[result.region, result.country].filter(Boolean).join(', ')}
                </span>
              </li>
            ))
          )}
        </ul>
      )}
    </form>
  );
}
