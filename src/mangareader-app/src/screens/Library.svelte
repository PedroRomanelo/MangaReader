<script lang="ts">
  import { onMount } from 'svelte';
  import { listLibrary, NotConfiguredError, type LibraryItem } from '../lib/api';
  import { go } from '../lib/router';

  let items: LibraryItem[] = [];
  let loading = true;
  let error: string | null = null;

  async function load() {
    loading = true;
    error = null;
    try {
      items = await listLibrary();
    } catch (e) {
      if (e instanceof NotConfiguredError) {
        go({ name: 'settings' });
        return;
      }
      error = e instanceof Error ? e.message : String(e);
    } finally {
      loading = false;
    }
  }

  onMount(load);
</script>

<main class="app-shell">
  <div class="topbar">
    <h1>Biblioteca</h1>
    <button on:click={load} disabled={loading}>{loading ? '…' : 'Atualizar'}</button>
    <button on:click={() => go({ name: 'settings' })}>Ajustes</button>
  </div>

  {#if error}
    <div class="error-banner">{error}</div>
  {/if}

  {#if loading && items.length === 0}
    <p class="muted">Carregando…</p>
  {:else if !loading && items.length === 0}
    <p class="muted">
      Vazio. Adicione mangás pelo backend (por enquanto <code>POST /api/manga</code>).
    </p>
  {/if}

  <ul class="grid">
    {#each items as m (m.id)}
      <li>
        <button class="card" on:click={() => go({ name: 'manga', mangaId: m.id })}>
          <div class="cover">
            {#if m.coverUrl}
              <img src={m.coverUrl} alt="" loading="lazy" />
            {:else}
              <div class="cover-placeholder">?</div>
            {/if}
          </div>
          <div class="title">{m.title}</div>
          <div class="count muted">{m.downloadedChapters}/{m.totalChapters} caps</div>
        </button>
      </li>
    {/each}
  </ul>
</main>

<style>
  .grid {
    list-style: none;
    padding: 0;
    margin: 0;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
    gap: 12px;
  }
  .card {
    display: block;
    width: 100%;
    text-align: left;
    background: var(--bg-elev);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: 8px;
    cursor: pointer;
  }
  .card:hover { background: var(--bg-elev-2); }
  .cover {
    aspect-ratio: 2 / 3;
    background: var(--bg-elev-2);
    border-radius: 4px;
    overflow: hidden;
    margin-bottom: 8px;
  }
  .cover img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .cover-placeholder {
    width: 100%; height: 100%;
    display: flex; align-items: center; justify-content: center;
    color: var(--text-dim); font-size: 32px;
  }
  .title { font-size: 13px; line-height: 1.25; margin-bottom: 2px; }
  .count { font-size: 12px; }
  code { background: var(--bg-elev); padding: 1px 6px; border-radius: 4px; font-size: 12px; }
</style>
