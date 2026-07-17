<script lang="ts">
  import { onMount } from 'svelte';
  import { getServerUrl, setServerUrl } from '../lib/settings';
  import { health } from '../lib/api';
  import { go } from '../lib/router';

  let url = '';
  let testing = false;
  let status: 'idle' | 'ok' | 'fail' = 'idle';
  let saving = false;

  onMount(async () => {
    url = (await getServerUrl()) ?? '';
  });

  async function testConnection() {
    if (!url.trim()) return;
    testing = true;
    status = 'idle';
    try {
      await setServerUrl(url);
      status = (await health()) ? 'ok' : 'fail';
    } finally {
      testing = false;
    }
  }

  async function save() {
    if (!url.trim()) return;
    saving = true;
    try {
      await setServerUrl(url);
      go({ name: 'library' });
    } finally {
      saving = false;
    }
  }
</script>

<main class="app-shell">
  <div class="topbar">
    <h1>Ajustes</h1>
  </div>

  <p class="muted">
    URL do backend .NET. Ex.: <code>http://192.168.0.10:5000</code> (Wi-Fi) ou
    <code>http://192.168.42.129:5000</code> (USB tethering).
  </p>

  <label>
    <span>Servidor</span>
    <input
      type="url"
      bind:value={url}
      placeholder="http://192.168.0.10:5000"
      autocomplete="off"
      autocapitalize="off"
      spellcheck="false"
    />
  </label>

  <div class="actions">
    <button on:click={testConnection} disabled={!url.trim() || testing}>
      {testing ? 'Testando…' : 'Testar'}
    </button>
    <button class="primary" on:click={save} disabled={!url.trim() || saving}>
      {saving ? 'Salvando…' : 'Salvar'}
    </button>
  </div>

  {#if status === 'ok'}
    <p class="ok">Conexão ok.</p>
  {:else if status === 'fail'}
    <p class="fail">Não respondeu. Cheque a URL, se o backend está no ar e se você está na mesma rede.</p>
  {/if}
</main>

<style>
  label { display: block; margin-top: 12px; }
  label span { display: block; font-size: 13px; color: var(--text-dim); margin-bottom: 4px; }
  .actions { display: flex; gap: 8px; margin-top: 16px; }
  code { background: var(--bg-elev); padding: 1px 6px; border-radius: 4px; font-size: 12px; }
  .ok   { color: var(--ok);     margin-top: 12px; }
  .fail { color: var(--danger); margin-top: 12px; }
</style>
