<script>
  import { onMount } from 'svelte';
  import Books from 'phosphor-svelte/lib/Books';
  import UploadSimple from 'phosphor-svelte/lib/UploadSimple';
  import FileText from 'phosphor-svelte/lib/FileText';
  import Sparkle from 'phosphor-svelte/lib/Sparkle';
  import PaperPlaneRight from 'phosphor-svelte/lib/PaperPlaneRight';
  import Quotes from 'phosphor-svelte/lib/Quotes';
  import CheckCircle from 'phosphor-svelte/lib/CheckCircle';
  import Warning from 'phosphor-svelte/lib/Warning';
  import Database from 'phosphor-svelte/lib/Database';
  import Lightning from 'phosphor-svelte/lib/Lightning';
  import Sun from 'phosphor-svelte/lib/Sun';
  import Moon from 'phosphor-svelte/lib/Moon';
  import { traced } from './tracing.js';

  const suggestions = [
    'How much parental leave do I get?',
    'How much is the home office stipend, and what can I buy with it?',
    'How many PTO days can I carry over into next year?',
    'What do I need to do in my first week?'
  ];

  let documents = $state([]);
  let totalChunks = $state(0);
  let question = $state('');
  let result = $state(null);
  let askedQuestion = $state('');
  let uploading = $state(false);
  let indexing = $state(false);
  let asking = $state(false);
  let notice = $state('');
  let error = $state('');
  let dragOver = $state(false);
  let activeCitation = $state(null);

  let theme = $state(
    typeof document !== 'undefined' && document.documentElement.dataset.theme === 'light' ? 'light' : 'dark'
  );

  // Split the answer into text and `[n]` citation markers so markers render as chips.
  let answerParts = $derived(
    result
      ? result.answer.split(/(\[\d+\])/g).filter(Boolean).map((part) => {
          const m = part.match(/^\[(\d+)\]$/);
          return m ? { cite: Number(m[1]) } : { text: part };
        })
      : []
  );

  function toggleTheme() {
    theme = theme === 'dark' ? 'light' : 'dark';
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem('handbook-theme', theme);
    } catch {
      /* storage may be unavailable; theme still applies for this session */
    }
  }

  async function problemMessage(res, fallback) {
    const body = await res.json().catch(() => null);
    return body?.detail || body?.title || fallback;
  }

  function flash(message) {
    notice = message;
    setTimeout(() => (notice = ''), 5000);
  }

  async function loadDocuments() {
    try {
      const res = await fetch('/api/documents');
      if (!res.ok) throw new Error(await problemMessage(res, 'Could not load documents'));
      const data = await res.json();
      documents = data.documents;
      totalChunks = data.total_chunks;
    } catch (err) {
      console.error('Failed to load documents:', err);
    }
  }

  async function indexSamples() {
    indexing = true;
    error = '';
    try {
      const data = await traced('index sample documents', {}, async () => {
        const res = await fetch('/api/documents/samples', { method: 'POST' });
        if (!res.ok) throw new Error(await problemMessage(res, 'Indexing failed'));
        return res.json();
      });
      flash(data.message);
      await loadDocuments();
    } catch (err) {
      error = err.message;
    } finally {
      indexing = false;
    }
  }

  async function handleFileUpload(file) {
    if (!file) return;
    uploading = true;
    error = '';
    try {
      const formData = new FormData();
      formData.append('file', file);
      const data = await traced('upload document', { 'file.size': file.size }, async () => {
        const res = await fetch('/api/documents', { method: 'POST', body: formData });
        if (!res.ok) throw new Error(await problemMessage(res, 'Upload failed'));
        return res.json();
      });
      flash(data.message);
      await loadDocuments();
    } catch (err) {
      error = `Upload failed: ${err.message}`;
    } finally {
      uploading = false;
      const input = document.getElementById('fileInput');
      if (input) input.value = '';
    }
  }

  function onFileDrop(event) {
    event.preventDefault();
    dragOver = false;
    const file = event.dataTransfer.files[0];
    if (file && file.name.toLowerCase().endsWith('.txt')) {
      handleFileUpload(file);
    } else {
      error = 'Please upload a .txt file';
    }
  }

  async function ask(text = question) {
    const q = text.trim();
    if (!q) return;
    question = q;
    asking = true;
    error = '';
    activeCitation = null;
    try {
      result = await traced('ask question', { 'app.question.length': q.length }, async () => {
        const res = await fetch('/api/ask', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ question: q })
        });
        if (!res.ok) throw new Error(await problemMessage(res, 'Failed to get an answer'));
        return res.json();
      });
      askedQuestion = q;
    } catch (err) {
      error = err.message;
    } finally {
      asking = false;
    }
  }

  function focusCitation(n) {
    activeCitation = n;
    document.getElementById(`source-${n}`)?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  onMount(loadDocuments);
</script>

<a class="skip-link" href="#main">Skip to content</a>

<header class="topbar">
  <div class="topbar-inner">
    <div class="brand">
      <span class="brand-mark" aria-hidden="true"><Books size={22} weight="fill" /></span>
      <span class="brand-text">
        <span class="brand-name">Contoso Handbook Q&amp;A</span>
        <span class="brand-tag">Answers grounded in company policy</span>
      </span>
    </div>
    <div class="topbar-actions">
      <span class="stack" aria-label="Built with">Svelte · FastAPI · Qdrant · OpenAI · Aspire</span>
      <button type="button" class="icon-btn" onclick={toggleTheme} aria-pressed={theme === 'dark'}>
        {#if theme === 'dark'}
          <Sun size={18} weight="bold" aria-hidden="true" />
        {:else}
          <Moon size={18} weight="bold" aria-hidden="true" />
        {/if}
        <span class="sr-only">Switch to {theme === 'dark' ? 'light' : 'dark'} theme</span>
      </button>
    </div>
  </div>
</header>

<main id="main" class="layout">
  <section class="panel ask-panel" aria-labelledby="ask-heading">
    <h1 id="ask-heading">Ask the handbook</h1>
    <p class="lede">
      Questions are answered from the indexed policy documents. Every claim cites the exact passage it came from.
    </p>

    <form
      class="ask-form"
      onsubmit={(e) => {
        e.preventDefault();
        ask();
      }}
    >
      <label class="sr-only" for="question">Your question</label>
      <textarea
        id="question"
        rows="2"
        bind:value={question}
        placeholder="e.g. How many sick days do I get?"
        disabled={asking}
        onkeydown={(e) => {
          if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            ask();
          }
        }}
      ></textarea>
      <button class="primary-btn" type="submit" disabled={asking || !question.trim()}>
        {#if asking}
          <span class="spinner" aria-hidden="true"></span> Searching…
        {:else}
          <PaperPlaneRight size={18} weight="fill" aria-hidden="true" /> Ask
        {/if}
      </button>
    </form>

    <ul class="suggestions" aria-label="Suggested questions">
      {#each suggestions as s}
        <li><button type="button" class="chip" onclick={() => ask(s)} disabled={asking}>{s}</button></li>
      {/each}
    </ul>

    {#if error}
      <div class="alert error" role="alert"><Warning size={18} weight="fill" aria-hidden="true" /> {error}</div>
    {/if}

    <div aria-live="polite">
      {#if result}
        <article class="answer">
          <header class="answer-head">
            <span class="answer-label"><Sparkle size={16} weight="fill" aria-hidden="true" /> Answer</span>
            <span class="asked">“{askedQuestion}”</span>
          </header>
          <p class="answer-body">
            {#each answerParts as part}
              {#if part.cite}
                <button
                  type="button"
                  class="cite"
                  class:active={activeCitation === part.cite}
                  onclick={() => focusCitation(part.cite)}
                  aria-label={`Show source ${part.cite}`}>{part.cite}</button
                >
              {:else}{part.text}{/if}
            {/each}
          </p>
          <footer class="answer-meta">
            <span><Lightning size={14} weight="fill" aria-hidden="true" /> {result.model}</span>
            <span>{result.usage.input_tokens.toLocaleString()} in · {result.usage.output_tokens.toLocaleString()} out tokens</span>
            <span><Database size={14} weight="fill" aria-hidden="true" /> retrieval {Math.round(result.retrieval_ms)} ms</span>
          </footer>
        </article>

        <h2 class="sources-heading">
          <Quotes size={16} weight="fill" aria-hidden="true" /> Sources
          <span class="muted">top {result.citations.length} chunks by similarity</span>
        </h2>
        <ol class="sources">
          {#each result.citations as c (c.chunk_id)}
            <li
              id={`source-${c.number}`}
              class="source"
              class:cited={c.cited}
              class:active={activeCitation === c.number}
            >
              <div class="source-head">
                <span class="source-num" aria-hidden="true">{c.number}</span>
                <span class="source-title">
                  <strong>{c.section || 'Overview'}</strong>
                  <span class="source-doc" title={c.title}>{c.title}</span>
                </span>
                <span class="score" title="Cosine similarity">
                  <span class="score-bar"><span style={`width:${Math.max(4, Math.round(c.score * 100))}%`}></span></span>
                  {c.score.toFixed(2)}
                </span>
              </div>
              <p class="source-text">{c.text}</p>
              <div class="source-foot">
                <code>{c.chunk_id}</code>
                {#if c.cited}<span class="badge">Cited</span>{:else}<span class="badge muted-badge">Not cited</span>{/if}
              </div>
            </li>
          {/each}
        </ol>
      {:else if !asking}
        <div class="empty">
          <Sparkle size={28} weight="duotone" aria-hidden="true" />
          <p>
            {documents.length
              ? 'Pick a suggested question or ask your own.'
              : 'Index the sample documents to get started.'}
          </p>
        </div>
      {/if}
    </div>
  </section>

  <aside class="panel library" aria-labelledby="lib-heading">
    <div class="library-head">
      <h2 id="lib-heading">Knowledge base</h2>
      <span class="stat">{documents.length} docs · {totalChunks} chunks</span>
    </div>

    <ul class="doc-list">
      {#each documents as doc (doc.name)}
        <li class="doc">
          <FileText size={20} weight="duotone" aria-hidden="true" />
          <span class="doc-text">
            <span class="doc-title">{doc.title}</span>
            <span class="doc-name">{doc.name}</span>
          </span>
          <span class="doc-chunks" title="Chunks">{doc.chunks}</span>
        </li>
      {:else}
        <li class="doc-empty">No documents indexed yet.</li>
      {/each}
    </ul>

    <button type="button" class="secondary-btn" onclick={indexSamples} disabled={indexing}>
      {#if indexing}
        <span class="spinner" aria-hidden="true"></span> Indexing…
      {:else}
        <Database size={18} weight="duotone" aria-hidden="true" />
        {documents.length ? 'Re-index sample documents' : 'Index sample documents'}
      {/if}
    </button>

    <div
      class="dropzone"
      class:dragover={dragOver}
      ondrop={onFileDrop}
      ondragover={(e) => e.preventDefault()}
      ondragenter={() => (dragOver = true)}
      ondragleave={() => (dragOver = false)}
      role="button"
      tabindex="0"
      aria-label="Upload a .txt document. Drop a file here, or activate to browse."
      onclick={() => document.getElementById('fileInput')?.click()}
      onkeydown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          document.getElementById('fileInput')?.click();
        }
      }}
    >
      {#if uploading}
        <span class="spinner" aria-hidden="true"></span>
        <span>Embedding &amp; indexing…</span>
      {:else}
        <UploadSimple size={22} weight="duotone" aria-hidden="true" />
        <span><strong>Upload a .txt policy</strong><br /><small>Use <code>##</code> headings for section-level citations</small></span>
      {/if}
    </div>
    <input
      id="fileInput"
      type="file"
      accept=".txt"
      aria-label="Choose a .txt document to upload"
      onchange={(e) => handleFileUpload(e.target.files[0])}
    />

    {#if notice}
      <p class="alert success" role="status"><CheckCircle size={18} weight="fill" aria-hidden="true" /> {notice}</p>
    {/if}
  </aside>
</main>

<style>
  /* ---------- Top bar ---------- */
  .topbar {
    position: sticky;
    top: 0;
    z-index: 10;
    background: color-mix(in srgb, var(--bg) 88%, transparent);
    backdrop-filter: blur(10px);
    border-bottom: 1px solid var(--border);
  }

  .topbar-inner {
    max-width: var(--maxw);
    margin: 0 auto;
    padding: 0.8rem clamp(1rem, 3vw, 2rem);
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
  }

  .brand {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    min-width: 0;
  }

  .brand-mark {
    display: grid;
    place-items: center;
    width: 38px;
    height: 38px;
    border-radius: 10px;
    background: linear-gradient(135deg, var(--accent), var(--accent-2));
    color: #fff;
    flex-shrink: 0;
  }

  .brand-text {
    display: flex;
    flex-direction: column;
    line-height: 1.2;
    min-width: 0;
  }

  .brand-name {
    font-weight: 700;
    font-size: 1.05rem;
  }

  .brand-tag {
    font-size: 0.8rem;
    color: var(--ink-soft);
  }

  .topbar-actions {
    display: flex;
    align-items: center;
    gap: 0.9rem;
  }

  .stack {
    font-size: 0.8rem;
    color: var(--ink-soft);
  }

  .icon-btn {
    display: grid;
    place-items: center;
    width: 38px;
    height: 38px;
    border-radius: 10px;
    border: 1px solid var(--border);
    background: var(--panel);
    color: var(--ink);
    cursor: pointer;
  }

  .icon-btn:hover {
    border-color: var(--accent);
    color: var(--accent);
  }

  /* ---------- Layout ---------- */
  .layout {
    max-width: var(--maxw);
    margin: 0 auto;
    padding: 1.5rem clamp(1rem, 3vw, 2rem) 3rem;
    display: grid;
    grid-template-columns: minmax(0, 1fr) 340px;
    gap: 1.5rem;
    align-items: start;
  }

  .panel {
    background: var(--panel);
    border: 1px solid var(--border);
    border-radius: var(--radius-lg);
    box-shadow: var(--shadow-md);
    padding: 1.5rem;
  }

  h1 {
    font-size: clamp(1.5rem, 2.6vw, 1.9rem);
    font-weight: 750;
    letter-spacing: -0.02em;
  }

  .lede {
    margin-top: 0.35rem;
    color: var(--ink-muted);
  }

  /* ---------- Ask ---------- */
  .ask-form {
    display: flex;
    gap: 0.6rem;
    margin-top: 1.1rem;
    align-items: stretch;
  }

  textarea {
    flex: 1;
    min-width: 0;
    padding: 0.75rem 0.95rem;
    border: 1px solid var(--border-strong);
    border-radius: var(--radius-md);
    background: var(--panel-2);
    color: var(--ink);
    resize: vertical;
    line-height: 1.5;
    min-height: 3.2rem;
  }

  textarea::placeholder {
    color: var(--ink-soft);
  }

  textarea:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 1px;
  }

  .primary-btn,
  .secondary-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.45rem;
    border-radius: var(--radius-md);
    font-weight: 650;
    cursor: pointer;
    transition: background 0.15s ease, border-color 0.15s ease;
  }

  .primary-btn {
    padding: 0 1.3rem;
    border: none;
    background: var(--accent);
    color: var(--accent-contrast);
  }

  .primary-btn:hover:not(:disabled) {
    background: var(--accent-strong);
  }

  .primary-btn:disabled,
  .secondary-btn:disabled,
  .chip:disabled {
    opacity: 0.55;
    cursor: not-allowed;
  }

  .suggestions {
    list-style: none;
    display: flex;
    flex-wrap: wrap;
    gap: 0.45rem;
    margin-top: 0.8rem;
  }

  .chip {
    padding: 0.35rem 0.75rem;
    border-radius: 999px;
    border: 1px solid var(--border);
    background: var(--panel-2);
    color: var(--ink-muted);
    font-size: 0.83rem;
    cursor: pointer;
    text-align: left;
  }

  .chip:hover:not(:disabled) {
    border-color: var(--accent);
    color: var(--ink);
  }

  /* ---------- Answer ---------- */
  .answer {
    margin-top: 1.4rem;
    padding: 1.2rem 1.3rem;
    border-radius: var(--radius-md);
    background: var(--accent-soft);
    border: 1px solid var(--accent-border);
  }

  .answer-head {
    display: flex;
    align-items: baseline;
    gap: 0.75rem;
    flex-wrap: wrap;
    margin-bottom: 0.55rem;
  }

  .answer-label {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    font-size: 0.78rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    color: var(--accent);
  }

  .asked {
    color: var(--ink-soft);
    font-size: 0.88rem;
    font-style: italic;
  }

  .answer-body {
    font-size: 1.04rem;
    line-height: 1.7;
  }

  .cite {
    display: inline-grid;
    place-items: center;
    min-width: 1.35rem;
    height: 1.35rem;
    padding: 0 0.3rem;
    margin: 0 0.1rem;
    border-radius: 6px;
    border: none;
    background: var(--accent);
    color: var(--accent-contrast);
    font-size: 0.75rem;
    font-weight: 700;
    vertical-align: 0.15em;
    cursor: pointer;
  }

  .cite.active,
  .cite:hover {
    background: var(--accent-strong);
  }

  .answer-meta {
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem 1.1rem;
    margin-top: 0.9rem;
    font-size: 0.8rem;
    color: var(--ink-soft);
  }

  .answer-meta span {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
  }

  .sources-heading {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    margin: 1.4rem 0 0.7rem;
    font-size: 0.95rem;
    font-weight: 700;
  }

  .muted {
    font-weight: 400;
    color: var(--ink-soft);
    font-size: 0.82rem;
  }

  .sources {
    list-style: none;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
    gap: 0.7rem;
  }

  .source {
    display: flex;
    flex-direction: column;
    gap: 0.45rem;
    padding: 0.8rem 0.95rem;
    border-radius: var(--radius-md);
    border: 1px solid var(--border);
    background: var(--panel-2);
    opacity: 0.72;
    transition: border-color 0.15s ease, opacity 0.15s ease;
  }

  .source.cited {
    opacity: 1;
  }

  .source.active {
    border-color: var(--accent);
    box-shadow: 0 0 0 3px var(--accent-ring);
  }

  .source-head {
    display: flex;
    align-items: flex-start;
    gap: 0.6rem;
  }

  .source-num {
    display: grid;
    place-items: center;
    width: 1.5rem;
    height: 1.5rem;
    border-radius: 6px;
    background: var(--panel-3);
    color: var(--ink-muted);
    font-size: 0.78rem;
    font-weight: 700;
    flex-shrink: 0;
  }

  .source.cited .source-num {
    background: var(--accent);
    color: var(--accent-contrast);
  }

  .source-title {
    display: flex;
    flex-direction: column;
    min-width: 0;
    flex: 1;
    font-size: 0.86rem;
    line-height: 1.3;
  }

  .source-title strong {
    color: var(--accent);
  }

  .source-doc {
    color: var(--ink-soft);
    font-size: 0.8rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .score {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    font-size: 0.78rem;
    font-variant-numeric: tabular-nums;
    color: var(--ink-muted);
    flex-shrink: 0;
  }

  .score-bar {
    width: 42px;
    height: 6px;
    border-radius: 3px;
    background: var(--panel-3);
    overflow: hidden;
  }

  .score-bar span {
    display: block;
    height: 100%;
    background: var(--accent);
  }

  .source-text {
    font-size: 0.85rem;
    color: var(--ink-muted);
    line-height: 1.55;
    display: -webkit-box;
    -webkit-line-clamp: 3;
    line-clamp: 3;
    -webkit-box-orient: vertical;
    overflow: hidden;
  }

  .source-foot {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
    margin-top: auto;
  }

  code {
    font-family: var(--font-mono);
    font-size: 0.76rem;
    color: var(--ink-soft);
  }

  .badge {
    font-size: 0.72rem;
    font-weight: 700;
    padding: 0.12rem 0.5rem;
    border-radius: 999px;
    background: var(--success-soft);
    color: var(--success);
  }

  .muted-badge {
    background: var(--panel-3);
    color: var(--ink-soft);
  }

  .empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.5rem;
    margin-top: 1.5rem;
    padding: 2.5rem 1rem;
    border: 1px dashed var(--border-strong);
    border-radius: var(--radius-md);
    color: var(--ink-soft);
  }

  /* ---------- Library ---------- */
  .library {
    display: flex;
    flex-direction: column;
    gap: 1rem;
    position: sticky;
    top: 5.2rem;
  }

  .library-head {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 0.5rem;
  }

  .library-head h2 {
    font-size: 1.05rem;
    font-weight: 700;
  }

  .stat {
    font-size: 0.8rem;
    color: var(--ink-soft);
  }

  .doc-list {
    list-style: none;
    display: flex;
    flex-direction: column;
    gap: 0.45rem;
  }

  .doc {
    display: flex;
    align-items: center;
    gap: 0.65rem;
    padding: 0.6rem 0.7rem;
    border-radius: var(--radius-sm);
    background: var(--panel-2);
    border: 1px solid var(--border);
  }

  .doc :global(svg) {
    color: var(--accent);
    flex-shrink: 0;
  }

  .doc-text {
    display: flex;
    flex-direction: column;
    min-width: 0;
    flex: 1;
  }

  .doc-title {
    font-size: 0.86rem;
    font-weight: 600;
    line-height: 1.3;
  }

  .doc-name {
    font-family: var(--font-mono);
    font-size: 0.72rem;
    color: var(--ink-soft);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .doc-chunks {
    font-size: 0.74rem;
    font-weight: 700;
    padding: 0.1rem 0.45rem;
    border-radius: 999px;
    background: var(--panel-3);
    color: var(--ink-muted);
  }

  .doc-empty {
    padding: 1rem;
    text-align: center;
    color: var(--ink-soft);
    font-size: 0.9rem;
  }

  .secondary-btn {
    padding: 0.65rem 1rem;
    border: 1px solid var(--accent-border);
    background: var(--accent-soft);
    color: var(--accent);
  }

  .secondary-btn:hover:not(:disabled) {
    border-color: var(--accent);
  }

  .dropzone {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 0.85rem 1rem;
    border: 1.5px dashed var(--border-strong);
    border-radius: var(--radius-md);
    color: var(--ink-muted);
    font-size: 0.86rem;
    line-height: 1.35;
    cursor: pointer;
  }

  .dropzone :global(svg) {
    color: var(--accent);
    flex-shrink: 0;
  }

  .dropzone small {
    color: var(--ink-soft);
  }

  .dropzone:hover,
  .dropzone:focus-visible,
  .dropzone.dragover {
    border-color: var(--accent);
    background: var(--accent-soft);
  }

  input[type='file'] {
    display: none;
  }

  /* ---------- Alerts ---------- */
  .alert {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.65rem 0.9rem;
    border-radius: var(--radius-sm);
    font-size: 0.88rem;
    font-weight: 600;
  }

  .ask-panel .alert {
    margin-top: 1rem;
  }

  .alert.success {
    background: var(--success-soft);
    color: var(--success);
  }

  .alert.error {
    background: var(--danger-soft);
    color: var(--danger);
  }

  .spinner {
    width: 16px;
    height: 16px;
    border: 2px solid currentColor;
    border-right-color: transparent;
    border-radius: 50%;
    animation: spin 0.8s linear infinite;
    flex-shrink: 0;
  }

  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }

  @media (max-width: 960px) {
    .layout {
      grid-template-columns: 1fr;
    }

    .library {
      position: static;
    }

    .stack {
      display: none;
    }
  }

  @media (max-width: 520px) {
    .panel {
      padding: 1.1rem;
    }

    .ask-form {
      flex-direction: column;
    }

    .primary-btn {
      padding: 0.7rem 1rem;
    }

    .brand-tag {
      display: none;
    }
  }
</style>
