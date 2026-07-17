<script lang="ts">
  import { onMount } from 'svelte';
  import { route, go } from './lib/router';
  import { getServerUrl } from './lib/settings';
  import Library from './screens/Library.svelte';
  import MangaDetail from './screens/MangaDetail.svelte';
  import Settings from './screens/Settings.svelte';

  onMount(async () => {
    const url = await getServerUrl();
    if (!url) go({ name: 'settings' });
  });
</script>

{#if $route.name === 'settings'}
  <Settings />
{:else if $route.name === 'library'}
  <Library />
{:else if $route.name === 'manga'}
  <MangaDetail mangaId={$route.mangaId} />
{/if}
