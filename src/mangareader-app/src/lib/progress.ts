import { Preferences } from '@capacitor/preferences';

const KEY = 'readingProgress.v1';

export interface ProgressEntry {
  chapterMangadexId: string;
  lastPage: number;
  isRead: boolean;
  updatedAt: string; // ISO 8601
}

// Um único blob JSON em Preferences: 1 chave por capítulo estouraria
// o formato key/value e ficaria caro pra iterar no sync.
let cache: Record<string, ProgressEntry> | null = null;

async function loadAll(): Promise<Record<string, ProgressEntry>> {
  if (cache) return cache;
  const { value } = await Preferences.get({ key: KEY });
  if (value) {
    try {
      cache = JSON.parse(value) as Record<string, ProgressEntry>;
    } catch {
      cache = {};
    }
  } else {
    cache = {};
  }
  return cache;
}

async function persist(): Promise<void> {
  if (!cache) return;
  await Preferences.set({ key: KEY, value: JSON.stringify(cache) });
}

export async function getProgress(chapterMangadexId: string): Promise<ProgressEntry | null> {
  const all = await loadAll();
  return all[chapterMangadexId] ?? null;
}

export async function setProgress(
  chapterMangadexId: string,
  lastPage: number,
  isRead: boolean,
): Promise<void> {
  const all = await loadAll();
  all[chapterMangadexId] = {
    chapterMangadexId,
    lastPage,
    isRead,
    updatedAt: new Date().toISOString(),
  };
  await persist();
}

export async function getAllProgress(): Promise<ProgressEntry[]> {
  const all = await loadAll();
  return Object.values(all);
}
