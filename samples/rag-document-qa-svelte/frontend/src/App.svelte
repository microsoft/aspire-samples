<script>
  import { onMount } from 'svelte';
  import BookOpenText from 'phosphor-svelte/lib/BookOpenText';
  import UploadSimple from 'phosphor-svelte/lib/UploadSimple';
  import FileText from 'phosphor-svelte/lib/FileText';
  import ChatCircleText from 'phosphor-svelte/lib/ChatCircleText';
  import MagnifyingGlass from 'phosphor-svelte/lib/MagnifyingGlass';
  import Quotes from 'phosphor-svelte/lib/Quotes';
  import BookmarkSimple from 'phosphor-svelte/lib/BookmarkSimple';
  import CheckCircle from 'phosphor-svelte/lib/CheckCircle';
  import Warning from 'phosphor-svelte/lib/Warning';
  import Sun from 'phosphor-svelte/lib/Sun';
  import Moon from 'phosphor-svelte/lib/Moon';

  let documents = $state([]);
  let question = $state('');
  let answer = $state(null);
  let sources = $state([]);
  let uploading = $state(false);
  let asking = $state(false);
  let uploadMessage = $state('');
  let error = $state('');
  let dragOver = $state(false);

  let theme = $state(
    typeof document !== 'undefined' && document.documentElement.dataset.theme === 'light'
      ? 'light'
      : 'dark'
  );

  function toggleTheme() {
    theme = theme === 'dark' ? 'light' : 'dark';
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem('athenaeum-theme', theme);
    } catch {
      /* storage may be unavailable; theme still applies for this session */
    }
  }

  async function loadDocuments() {
    try {
      const res = await fetch('/documents');
      const data = await res.json();
      documents = data.documents;
    } catch (err) {
      console.error('Failed to load documents:', err);
    }
  }

  async function handleFileUpload(file) {
    if (!file) return;

    uploading = true;
    uploadMessage = '';
    error = '';

    try {
      const formData = new FormData();
      formData.append('file', file);

      const res = await fetch('/upload', {
        method: 'POST',
        body: formData
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => ({ detail: 'Upload failed' }));
        throw new Error(errorData.detail || 'Upload failed');
      }

      const data = await res.json();
      uploadMessage = `${data.message} (${data.chunks} chunks)`;
      await loadDocuments();

      // Clear the file input
      const fileInput = document.getElementById('fileInput');
      if (fileInput) fileInput.value = '';

      // Auto-clear success message after 5 seconds
      setTimeout(() => {
        uploadMessage = '';
      }, 5000);
    } catch (err) {
      error = `Upload failed: ${err.message}`;
      console.error('Upload error:', err);
    } finally {
      uploading = false;
    }
  }

  function onFileDrop(event) {
    event.preventDefault();
    dragOver = false;
    const file = event.dataTransfer.files[0];
    if (file && file.type === 'text/plain') {
      handleFileUpload(file);
    } else {
      error = 'Please upload a .txt file';
      setTimeout(() => {
        error = '';
      }, 3000);
    }
  }

  function onFileSelect(event) {
    const file = event.target.files[0];
    if (file) {
      handleFileUpload(file);
    }
  }

  async function askQuestion() {
    if (!question.trim()) return;

    asking = true;
    error = '';
    answer = null;
    sources = [];

    try {
      const res = await fetch('/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question: question.trim() })
      });

      if (!res.ok) {
        throw new Error('Failed to get answer');
      }

      const data = await res.json();
      answer = data.answer;
      sources = data.sources;
    } catch (err) {
      error = `Failed to get answer: ${err.message}`;
    } finally {
      asking = false;
    }
  }

  onMount(() => {
    loadDocuments();
  });
</script>

<a class="skip-link" href="#main">Skip to content</a>

<div class="page">
  <header class="masthead">
    <div class="brand">
      <span class="brand-mark" aria-hidden="true">
        <BookOpenText size={28} weight="duotone" />
      </span>
      <span class="brand-text">
        <span class="brand-name">Athenaeum</span>
        <span class="brand-tag">Aspire RAG document Q&amp;A</span>
      </span>
    </div>
    <button
      type="button"
      class="theme-toggle"
      onclick={toggleTheme}
      aria-pressed={theme === 'dark'}
    >
      {#if theme === 'dark'}
        <Sun size={20} weight="bold" aria-hidden="true" />
      {:else}
        <Moon size={20} weight="bold" aria-hidden="true" />
      {/if}
      <span class="sr-only">Switch to {theme === 'dark' ? 'light' : 'dark'} theme</span>
    </button>
  </header>

  <main id="main">
    <section class="intro">
      <h1>Ask your documents</h1>
      <p class="lede">
        Upload plain-text documents. Athenaeum chunks and embeds each one into a
        vector index, then retrieves the most relevant passages to ground every
        answer with citations.
      </p>
      <ul class="pillrow" aria-label="Built with">
        <li>Svelte</li>
        <li>Python</li>
        <li>OpenAI</li>
        <li>Qdrant</li>
        <li>RAG</li>
      </ul>
    </section>

    <div class="reading-room">
      {#if error}
        <div class="status error room-alert" role="alert">
          <Warning size={20} weight="fill" aria-hidden="true" />
          <span>{error}</span>
        </div>
      {/if}

      <!-- Acquisitions / Upload -->
      <section class="card shelf" aria-labelledby="acq-heading">
        <h2 id="acq-heading"><UploadSimple size={22} weight="duotone" aria-hidden="true" /> Upload documents</h2>

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
          onclick={(e) => {
            e.preventDefault();
            document.getElementById('fileInput')?.click();
          }}
          onkeydown={(e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              document.getElementById('fileInput')?.click();
            }
          }}
        >
          <span class="drop-icon" aria-hidden="true"><FileText size={38} weight="thin" /></span>
          <p class="drop-title">Drop a .txt file, or click to browse</p>
          <p class="drop-hint">Each document is chunked and embedded for semantic retrieval</p>
        </div>

        <input
          id="fileInput"
          type="file"
          accept=".txt"
          aria-label="Choose a .txt document to upload"
          onchange={onFileSelect}
        />

        <div class="status-region" aria-live="polite">
          {#if uploading}
            <p class="status loading"><span class="spinner" aria-hidden="true"></span> Uploading &amp; indexing…</p>
          {/if}
          {#if uploadMessage}
            <p class="status success"><CheckCircle size={18} weight="fill" aria-hidden="true" /> {uploadMessage}</p>
          {/if}
        </div>

        <h3 class="catalog-heading">
          Uploaded documents
          <span class="count">{documents.length}</span>
        </h3>

        <ul class="catalog">
          {#if documents.length === 0}
            <li class="catalog-empty">No documents yet. Upload one to begin.</li>
          {:else}
            {#each documents as doc}
              <li class="catalog-item">
                <FileText size={18} weight="duotone" aria-hidden="true" />
                <span>{doc}</span>
              </li>
            {/each}
          {/if}
        </ul>
      </section>

      <!-- Inquiry / Ask -->
      <section class="card desk" aria-labelledby="ask-heading">
        <h2 id="ask-heading"><ChatCircleText size={22} weight="duotone" aria-hidden="true" /> Ask a question</h2>

        <label class="sr-only" for="question">Your question</label>
        <textarea
          id="question"
          bind:value={question}
          placeholder="Ask a question about your documents…"
          disabled={asking}
        ></textarea>

        <button class="ask-btn" onclick={askQuestion} disabled={asking || !question.trim()}>
          {#if asking}
            <span class="spinner" aria-hidden="true"></span> Searching documents…
          {:else}
            <MagnifyingGlass size={18} weight="bold" aria-hidden="true" /> Ask question
          {/if}
        </button>

        <div class="answer-region" aria-live="polite">
          {#if answer}
            <article class="answer">
              <span class="answer-quote" aria-hidden="true"><Quotes size={26} weight="fill" /></span>
              <h3 class="answer-heading">Answer</h3>
              <p class="answer-body">{answer}</p>
            </article>

            {#if sources.length > 0}
              <div class="sources">
                <h3 class="sources-heading">Cited passages</h3>
                {#each sources as source}
                  <article class="source">
                    <header class="source-head">
                      <span class="source-name">
                        <BookmarkSimple size={16} weight="fill" aria-hidden="true" />
                        {source.filename}
                      </span>
                      <span class="source-score">{(source.score * 100).toFixed(1)}% match</span>
                    </header>
                    <p class="source-text">{source.text}</p>
                  </article>
                {/each}
              </div>
            {/if}
          {/if}
        </div>
      </section>
    </div>
  </main>

  <footer class="colophon">
    <p>Powered by <strong>Aspire</strong> — Svelte · Python · Qdrant · OpenAI</p>
    <p>Retrieval Augmented Generation over your documents with vector search.</p>
  </footer>
</div>

<style>
  .page {
    max-width: var(--maxw);
    margin: 0 auto;
    padding: 0 clamp(1rem, 4vw, 2.5rem) 3.5rem;
  }

  /* ---------- Masthead ---------- */
  .masthead {
    position: sticky;
    top: 0;
    z-index: 20;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 1rem 0 1.1rem;
    margin-bottom: 1.75rem;
    border-bottom: 2px solid var(--border-strong);
    background: var(--bg);
  }

  .brand {
    display: flex;
    align-items: center;
    gap: 0.85rem;
  }

  .brand-mark {
    display: grid;
    place-items: center;
    width: 46px;
    height: 46px;
    border-radius: 12px;
    background: var(--brass-soft);
    color: var(--brass);
    border: 1px solid var(--border-strong);
  }

  .brand-text {
    display: flex;
    flex-direction: column;
    line-height: 1.15;
  }

  .brand-name {
    font-family: var(--font-serif);
    font-size: 1.55rem;
    font-weight: 700;
    letter-spacing: 0.01em;
    color: var(--ink);
  }

  .brand-tag {
    font-size: 0.74rem;
    font-weight: 600;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--ink-soft);
  }

  .theme-toggle {
    display: grid;
    place-items: center;
    width: 44px;
    height: 44px;
    border-radius: 12px;
    background: var(--panel);
    border: 1px solid var(--border-strong);
    color: var(--ink);
    cursor: pointer;
    box-shadow: var(--shadow-sm);
    transition: background 0.18s ease, transform 0.18s ease, border-color 0.18s ease;
  }

  .theme-toggle:hover {
    background: var(--panel-2);
    border-color: var(--brass);
    color: var(--brass-strong);
  }

  .theme-toggle:active {
    transform: scale(0.95);
  }

  /* ---------- Intro ---------- */
  .intro {
    margin-bottom: 2rem;
  }

  .intro h1 {
    font-family: var(--font-serif);
    font-size: clamp(2rem, 4.5vw, 2.9rem);
    font-weight: 700;
    letter-spacing: -0.01em;
    color: var(--ink);
  }

  .lede {
    margin-top: 0.6rem;
    max-width: 68ch;
    font-size: 1.06rem;
    color: var(--ink-muted);
  }

  .pillrow {
    list-style: none;
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
    margin-top: 1.1rem;
  }

  .pillrow li {
    padding: 0.3rem 0.8rem;
    border-radius: 999px;
    background: var(--brass-soft);
    border: 1px solid var(--border-strong);
    color: var(--brass-strong);
    font-size: 0.8rem;
    font-weight: 600;
    letter-spacing: 0.02em;
  }

  /* ---------- Reading room layout ---------- */
  .reading-room {
    display: grid;
    grid-template-columns: 1.02fr 0.98fr;
    gap: 1.5rem;
    align-items: start;
  }

  .room-alert {
    grid-column: 1 / -1;
  }

  .card {
    background: var(--panel);
    border: 1px solid var(--border);
    border-radius: var(--radius-lg);
    padding: 1.75rem;
    box-shadow: var(--shadow-md);
  }

  .card h2 {
    display: flex;
    align-items: center;
    gap: 0.55rem;
    font-family: var(--font-serif);
    font-size: 1.3rem;
    font-weight: 700;
    color: var(--ink);
    margin-bottom: 1.25rem;
  }

  .card h2 :global(svg) {
    color: var(--brass);
  }

  /* ---------- Dropzone ---------- */
  .dropzone {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.55rem;
    padding: 2rem 1.25rem;
    border: 2px dashed var(--border-strong);
    border-radius: var(--radius-md);
    background: var(--panel-2);
    text-align: center;
    cursor: pointer;
    color: var(--ink);
    transition: border-color 0.2s ease, background 0.2s ease;
  }

  .dropzone:hover,
  .dropzone:focus-visible {
    border-color: var(--brass);
    background: var(--brass-soft);
  }

  .dropzone.dragover {
    border-color: var(--brass);
    background: var(--brass-soft);
  }

  .drop-icon {
    color: var(--brass);
  }

  .drop-title {
    font-weight: 600;
  }

  .drop-hint {
    font-size: 0.86rem;
    color: var(--ink-soft);
  }

  input[type='file'] {
    display: none;
  }

  /* ---------- Status messages ---------- */
  .status-region {
    margin-top: 0.85rem;
  }

  .status {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.7rem 0.95rem;
    border-radius: var(--radius-sm);
    font-weight: 600;
    font-size: 0.92rem;
    margin-bottom: 0.5rem;
  }

  .status.loading {
    color: var(--ink-muted);
  }

  .status.success {
    background: var(--success-soft);
    color: var(--success);
    border: 1px solid color-mix(in srgb, var(--success) 35%, transparent);
  }

  .status.error {
    background: var(--danger-soft);
    color: var(--danger-strong);
    border: 1px solid color-mix(in srgb, var(--danger) 40%, transparent);
  }

  .spinner {
    width: 18px;
    height: 18px;
    border: 2px solid var(--border-strong);
    border-top-color: var(--brass);
    border-radius: 50%;
    animation: spin 0.9s linear infinite;
    flex-shrink: 0;
  }

  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }

  /* ---------- Catalogue ---------- */
  .catalog-heading {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    font-family: var(--font-serif);
    font-size: 1.02rem;
    font-weight: 700;
    color: var(--ink-muted);
    margin: 1.5rem 0 0.85rem;
  }

  .count {
    display: inline-grid;
    place-items: center;
    min-width: 1.6rem;
    height: 1.6rem;
    padding: 0 0.45rem;
    border-radius: 999px;
    background: var(--brass-soft);
    color: var(--brass-strong);
    border: 1px solid var(--border-strong);
    font-family: var(--font-sans);
    font-size: 0.82rem;
    font-weight: 700;
  }

  .catalog {
    list-style: none;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    max-height: 230px;
    overflow-y: auto;
  }

  .catalog-item {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    padding: 0.7rem 0.85rem;
    border-radius: var(--radius-sm);
    background: var(--panel-2);
    border: 1px solid var(--border);
    color: var(--ink);
    font-size: 0.92rem;
  }

  .catalog-item :global(svg) {
    color: var(--brass);
    flex-shrink: 0;
  }

  .catalog-item span {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .catalog-empty {
    padding: 1rem;
    text-align: center;
    color: var(--ink-soft);
    font-style: italic;
  }

  /* ---------- Inquiry desk ---------- */
  textarea {
    width: 100%;
    min-height: 120px;
    padding: 0.85rem 1rem;
    border: 1px solid var(--border-strong);
    border-radius: var(--radius-md);
    background: var(--panel-2);
    color: var(--ink);
    resize: vertical;
    line-height: 1.5;
  }

  textarea::placeholder {
    color: var(--ink-soft);
  }

  textarea:focus-visible {
    outline: 3px solid var(--brass);
    outline-offset: 1px;
    border-color: var(--brass);
  }

  .ask-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 0.5rem;
    width: 100%;
    margin-top: 1rem;
    padding: 0.85rem 1.25rem;
    border: 1px solid var(--brass-strong);
    border-radius: var(--radius-md);
    background: var(--brass);
    color: var(--brass-contrast);
    font-weight: 700;
    cursor: pointer;
    transition: background 0.18s ease, transform 0.18s ease;
  }

  .ask-btn:hover:not(:disabled) {
    background: var(--brass-strong);
  }

  .ask-btn:active:not(:disabled) {
    transform: translateY(1px);
  }

  .ask-btn:disabled {
    opacity: 0.55;
    cursor: not-allowed;
  }

  /* ---------- Answer & sources ---------- */
  .answer-region:empty {
    display: none;
  }

  .answer {
    position: relative;
    margin-top: 1.5rem;
    padding: 1.4rem 1.5rem 1.5rem;
    border-radius: var(--radius-md);
    background: var(--panel-2);
    border: 1px solid var(--border);
    border-left: 4px solid var(--brass);
  }

  .answer-quote {
    position: absolute;
    top: 0.9rem;
    right: 1rem;
    color: var(--brass);
    opacity: 0.45;
  }

  .answer-heading {
    font-family: var(--font-serif);
    font-size: 0.85rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.12em;
    color: var(--brass-strong);
    margin-bottom: 0.6rem;
  }

  .answer-body {
    font-family: var(--font-serif);
    font-size: 1.1rem;
    line-height: 1.7;
    color: var(--ink);
  }

  .sources {
    margin-top: 1.5rem;
  }

  .sources-heading {
    font-family: var(--font-serif);
    font-size: 0.82rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.12em;
    color: var(--ink-muted);
    margin-bottom: 0.75rem;
  }

  .source {
    padding: 0.95rem 1.1rem;
    border-radius: var(--radius-sm);
    background: var(--panel-3);
    border: 1px solid var(--border);
    margin-bottom: 0.7rem;
  }

  .source-head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    margin-bottom: 0.5rem;
  }

  .source-name {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    font-weight: 700;
    color: var(--ink);
    font-size: 0.92rem;
  }

  .source-name :global(svg) {
    color: var(--brass);
    flex-shrink: 0;
  }

  .source-score {
    flex-shrink: 0;
    font-size: 0.82rem;
    font-weight: 700;
    color: var(--brass-strong);
  }

  .source-text {
    font-family: var(--font-serif);
    font-style: italic;
    color: var(--ink-muted);
    line-height: 1.6;
  }

  /* ---------- Colophon ---------- */
  .colophon {
    margin-top: 2.75rem;
    padding-top: 1.5rem;
    border-top: 1px solid var(--border);
    text-align: center;
    color: var(--ink-soft);
    font-size: 0.9rem;
  }

  .colophon strong {
    color: var(--ink-muted);
  }

  .colophon p + p {
    margin-top: 0.35rem;
  }

  @media (max-width: 900px) {
    .reading-room {
      grid-template-columns: 1fr;
    }
  }
</style>
