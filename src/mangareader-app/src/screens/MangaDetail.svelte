<script lang="ts">
  import { onMount } from 'svelte';
  import { getManifest, NotConfiguredError, type ManifestManga, type ManifestChapter } from '../lib/api';
  import { downloadChapter, deleteChapter, localChapterSize } from '../lib/storage';
  import { go } from '../lib/router';

  export let mangaId: number;

  let manga: ManifestManga | null = null;
  let loading = true;
  let error: string | null = null;

  // chapterId -> tamanho local em bytes (ou null se ausente)
  const localSizes = new Map<number, number | null>();
  // chapterId -> estado da UI
  const busy = new Map<number, 'downloading' | 'deleting'>();

  function humanBytes(n: number): string {
    if (n < 1024) return `${n} B`;
    if (n < 1024 * 1024) return `${(n / 1024).toFixed(0)} KB`;
    return `${(n / 1024 / 1024).toFixed(1)} MB`;
  }

  async function load() {
    loading = true;
    error = null;
    try {
      const all = await getManifest();
      manga = all.find((x) => x.mangaId === mangaId) ?? null;
      if (!manga) {
        error = `Mangá ${mangaId} não encontrado no manifest.`;
        return;
      }
      await refreshLocalSizes();
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

  async function refreshLocalSizes() {
    if (!manga) return;
    await Promise.all(
      manga.chapters.map(async (c) => {
        localSizes.set(c.chapterId, await localChapterSize(mangaId, c.chapterId));
      }),
    );
    // força reatividade
    manga = manga;
  }

  async function onDownload(c: ManifestChapter) {
    if (!c.hasFile) return;
    busy.set(c.chapterId, 'downloading');
    manga = manga;
    try {
      const { size } = await downloadChapter(mangaId, c.chapterId);
      localSizes.set(c.chapterId, size);
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    } finally {
      busy.delete(c.chapterId);
      manga = manga;
    }
  }

  async function onDelete(c: ManifestChapter) {
    busy.set(c.chapterId, 'deleting');
    manga = manga;
    try {
      await deleteChapter(mangaId, c.chapterId);
      localSizes.set(c.chapterId, null);
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    } finally {
      busy.delete(c.chapterId);
      manga = manga;
    }
  }

  onMount(load);
</script>

<main class="app-shell">
  <div class="topbar">
    <button class="back" on:click={() => go({ name: 'library' })} aria-label="Voltar">←</button>
    <h1>{manga?.title ?? '…'}</h1>
    <button on:click={load} disabled={loading}>{loading ? '…' : '↻'}</button>
  </div>

  {#if error}
    <div class="error-banner">{error}</div>
  {/if}

  {#if manga}
    <ul class="chapters">
      {#each manga.chapters as c (c.chapterId)}
        {@const local = localSizes.get(c.chapterId)}
        {@const state = busy.get(c.chapterId)}
        <li>
          <div class="ch-main">
            <div class="ch-title">
              Cap {c.chapter ?? '?'}
              {#if !c.hasFile}
                <span class="tag warn">sem arquivo no servidor</span>
              {:else if local != null}
                <span class="tag ok">no aparelho</span>
              {/if}
            </div>
            <div class="ch-sub muted">
              {#if c.fileSize}servidor: {humanBytes(c.fileSize)}{/if}
              {#if local != null}<span class="dot">•</span>local: {humanBytes(local)}{/if}
            </div>
          </div>
          <div class="ch-actions">
            {#if state === 'downloading'}
              <button disabled>baixando…</button>
            {:else if state === 'deleting'}
              <button disabled>apagando…</button>
            {:else if local != null}
              <button class="danger" on:click={() => onDelete(c)}>apagar</button>
            {:else}
              <button class="primary" on:click={() => onDownload(c)} disabled={!c.hasFile}>
                baixar
              </button>
            {/if}
          </div>
        </li>
      {/each}
    </ul>
  {/if}
</main>

<style>
  .chapters { list-style: none; padding: 0; margin: 0; }
  .chapters li {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 10px 8px;
    border-bottom: 1px solid var(--border);
  }
  .ch-main { flex: 1; min-width: 0; }
  .ch-title { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .ch-sub { font-size: 12px; margin-top: 2px; }
  .ch-actions button { min-width: 88px; }
  .tag {
    font-size: 11px;
    padding: 1px 6px;
    border-radius: 3px;
    border: 1px solid;
  }
  .tag.ok   { color: var(--ok);     border-color: var(--ok); }
  .tag.warn { color: var(--danger); border-color: var(--danger); }
  .dot { margin: 0 6px; opacity: 0.5; }
</style>
