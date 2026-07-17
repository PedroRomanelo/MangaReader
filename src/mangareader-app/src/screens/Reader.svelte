<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { Filesystem, Directory } from '@capacitor/filesystem';
  import JSZip from 'jszip';
  import { go } from '../lib/router';
  import { getProgress, setProgress } from '../lib/progress';

  export let mangaId: number;
  export let chapterId: number;
  export let chapterMangadexId: string;

  const IMG_EXT = /\.(jpe?g|png|webp|gif|bmp|avif)$/i;

  let pageUrls: string[] = [];
  let index = 0;
  let loading = true;
  let error: string | null = null;
  let overlayVisible = true;

  async function readCbz(): Promise<Blob | string> {
    // Sem encoding, o plugin retorna base64 no native e Blob no web (varia
    // por versão). Trato as duas formas mais abaixo.
    const { data } = await Filesystem.readFile({
      path: `mangareader/${mangaId}/${chapterId}.cbz`,
      directory: Directory.External,
    });
    return data;
  }

  async function load() {
    try {
      const raw = await readCbz();
      const zip = typeof raw === 'string'
        ? await JSZip.loadAsync(raw, { base64: true })
        : await JSZip.loadAsync(raw);

      const entries = Object.values(zip.files)
        .filter((f) => !f.dir && IMG_EXT.test(f.name))
        .sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));

      pageUrls = await Promise.all(
        entries.map(async (f) => {
          const blob = await f.async('blob');
          return URL.createObjectURL(blob);
        }),
      );

      const prev = await getProgress(chapterMangadexId);
      if (prev && prev.lastPage >= 0 && prev.lastPage < pageUrls.length) {
        index = prev.lastPage;
      }
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    } finally {
      loading = false;
    }
  }

  function persistPage() {
    if (pageUrls.length === 0) return;
    const isRead = index >= pageUrls.length - 1;
    // fire-and-forget: falha aqui não deve travar a leitura
    void setProgress(chapterMangadexId, index, isRead);
  }

  function goPage(delta: number) {
    const next = Math.max(0, Math.min(pageUrls.length - 1, index + delta));
    if (next !== index) {
      index = next;
      persistPage();
    }
  }

  function onKey(e: KeyboardEvent) {
    if (e.key === 'ArrowRight' || e.key === 'PageDown' || e.key === ' ') {
      e.preventDefault();
      goPage(1);
    } else if (e.key === 'ArrowLeft' || e.key === 'PageUp') {
      e.preventDefault();
      goPage(-1);
    } else if (e.key === 'Escape') {
      go({ name: 'manga', mangaId });
    }
  }

  function onZoneClick(e: MouseEvent) {
    const target = e.currentTarget as HTMLElement;
    const rect = target.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const third = rect.width / 3;
    if (x < third) goPage(-1);
    else if (x > third * 2) goPage(1);
    else overlayVisible = !overlayVisible;
  }

  onMount(load);
  onDestroy(() => {
    for (const u of pageUrls) URL.revokeObjectURL(u);
  });
</script>

<svelte:window on:keydown={onKey} />

<div
  class="reader"
  on:click={onZoneClick}
  on:keydown|stopPropagation
  role="button"
  tabindex="0"
  aria-label="Área de leitura"
>
  {#if loading}
    <div class="msg">Carregando…</div>
  {:else if error}
    <div class="msg error">{error}</div>
  {:else if pageUrls.length === 0}
    <div class="msg">Sem imagens no .cbz.</div>
  {:else}
    <img src={pageUrls[index]} alt="Página {index + 1}" class="page" draggable="false" />
    {#if index + 1 < pageUrls.length}
      <img src={pageUrls[index + 1]} alt="" class="preload" aria-hidden="true" />
    {/if}
  {/if}

  {#if overlayVisible && !loading && !error && pageUrls.length > 0}
    <div class="overlay" on:click|stopPropagation role="presentation">
      <button
        class="btn"
        on:click|stopPropagation={() => go({ name: 'manga', mangaId })}
        aria-label="Voltar"
      >←</button>
      <div class="counter">{index + 1} / {pageUrls.length}</div>
    </div>
  {/if}
</div>

<style>
  .reader {
    position: fixed;
    inset: 0;
    background: #000;
    display: flex;
    align-items: center;
    justify-content: center;
    user-select: none;
    -webkit-user-select: none;
    overflow: hidden;
    cursor: pointer;
    outline: none;
  }
  .page {
    max-width: 100%;
    max-height: 100vh;
    object-fit: contain;
    display: block;
    pointer-events: none;
  }
  .preload {
    position: absolute;
    width: 1px;
    height: 1px;
    opacity: 0;
    pointer-events: none;
  }
  .msg { color: var(--text-dim); font-size: 14px; }
  .msg.error { color: var(--danger); padding: 16px; text-align: center; }
  .overlay {
    position: absolute;
    top: 0; left: 0; right: 0;
    padding: 12px 16px;
    display: flex;
    align-items: center;
    gap: 12px;
    background: linear-gradient(to bottom, rgba(0, 0, 0, 0.75), transparent);
    color: var(--text);
    cursor: default;
  }
  .btn {
    background: transparent;
    border: none;
    color: inherit;
    font-size: 20px;
    padding: 4px 12px;
    cursor: pointer;
  }
  .counter {
    margin-left: auto;
    font-variant-numeric: tabular-nums;
    font-size: 13px;
    color: var(--text-dim);
  }
</style>
