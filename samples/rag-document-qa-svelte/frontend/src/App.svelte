<script>
  import { onMount } from 'svelte';

  let documents = $state([]);
  let question = $state('');
  let answer = $state(null);
  let sources = $state([]);
  let uploading = $state(false);
  let asking = $state(false);
  let uploadMessage = $state('');
  let error = $state('');

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
      uploadMessage = `✓ ${data.message} (${data.chunks} chunks)`;
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
    event.currentTarget.classList.remove('dragover');
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

<main>
  <h1>📚 RAG Document Q&A</h1>
  <p class="subtitle">
    <span class="badge">Svelte</span>
    <span class="badge">Python</span>
    <span class="badge">OpenAI</span>
    <span class="badge">Qdrant</span>
    <span class="badge">RAG</span>
  </p>

  <div class="grid">
    <!-- Upload Section -->
    <div class="card">
      <h2>📤 Upload Documents</h2>

      <div
        class="upload-zone"
        ondrop={onFileDrop}
        ondragover={(e) => e.preventDefault()}
        ondragenter={(e) => e.currentTarget.classList.add('dragover')}
        ondragleave={(e) => e.currentTarget.classList.remove('dragover')}
        role="button"
        tabindex="0"
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
        <p style="font-size: 2rem; margin-bottom: 0.5rem;">📄</p>
        <p>Drop a .txt file here or click to browse</p>
        <p style="font-size: 0.85rem; color: #6b7280; margin-top: 0.5rem;">
          Documents will be chunked and embedded for semantic search
        </p>
      </div>

      <input
        id="fileInput"
        type="file"
        accept=".txt"
        onchange={onFileSelect}
      />

      {#if uploading}
        <p class="loading">Uploading and indexing...</p>
      {/if}

      {#if uploadMessage}
        <p class="success">{uploadMessage}</p>
      {/if}

      {#if error}
        <div class="error">{error}</div>
      {/if}

      <h3 style="margin-top: 1.5rem; margin-bottom: 0.75rem; color: #555;">
        Indexed Documents ({documents.length})
      </h3>

      <div class="document-list">
        {#if documents.length === 0}
          <p class="loading">No documents yet. Upload some to get started!</p>
        {:else}
          {#each documents as doc}
            <div class="document-item">
              📄 {doc}
            </div>
          {/each}
        {/if}
      </div>
    </div>

    <!-- Q&A Section -->
    <div class="card">
      <h2>💬 Ask Questions</h2>

      <textarea
        bind:value={question}
        placeholder="Ask a question about your documents..."
        disabled={asking}
      ></textarea>

      <button onclick={askQuestion} disabled={asking || !question.trim()}>
        {asking ? 'Thinking...' : 'Ask Question'}
      </button>

      {#if error}
        <div class="error">{error}</div>
      {/if}

      {#if answer}
        <div class="answer">
          <strong style="display: block; margin-bottom: 0.75rem;">Answer:</strong>
          {answer}
        </div>

        {#if sources.length > 0}
          <div class="sources">
            <strong style="display: block; margin-bottom: 0.75rem;">Sources:</strong>
            {#each sources as source, i}
              <div class="source">
                <div class="source-header">
                  <span>{source.filename}</span>
                  <span style="color: #667eea;">
                    Similarity: {(source.score * 100).toFixed(1)}%
                  </span>
                </div>
                <div class="source-text">{source.text}</div>
              </div>
            {/each}
          </div>
        {/if}
      {/if}
    </div>
  </div>

  <div style="text-align: center; color: rgba(255,255,255,0.8); font-size: 0.9rem;">
    <p>Powered by .NET Aspire 13 + Svelte + Python + Qdrant + OpenAI</p>
    <p style="margin-top: 0.5rem;">
      Demonstrates Retrieval Augmented Generation (RAG) with vector search
    </p>
  </div>
</main>
