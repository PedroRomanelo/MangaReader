import { getServerUrl } from './settings';

export class NotConfiguredError extends Error {
  constructor() {
    super('Servidor não configurado. Vá em Ajustes.');
    this.name = 'NotConfiguredError';
  }
}

async function baseUrl(): Promise<string> {
  const url = await getServerUrl();
  if (!url) throw new NotConfiguredError();
  return url;
}

async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const base = await baseUrl();
  const res = await fetch(`${base}${path}`, init);
  if (!res.ok) {
    throw new Error(`HTTP ${res.status} em ${path}`);
  }
  return res;
}

export interface LibraryItem {
  id: number;
  mangadexId: string;
  title: string;
  coverUrl: string | null;
  totalChapters: number;
  downloadedChapters: number;
}

export interface ManifestChapter {
  chapterId: number;
  mangadexId: string;
  chapter: string | null;
  downloadStatus: string;
  hasFile: boolean;
  fileSize: number | null;
  pageCount: number;
}

export interface ManifestManga {
  mangaId: number;
  mangadexId: string;
  title: string;
  chapters: ManifestChapter[];
}

export async function health(): Promise<boolean> {
  try {
    await apiFetch('/api/health');
    return true;
  } catch {
    return false;
  }
}

export async function listLibrary(): Promise<LibraryItem[]> {
  const res = await apiFetch('/api/manga');
  return res.json() as Promise<LibraryItem[]>;
}

export async function getManifest(): Promise<ManifestManga[]> {
  const res = await apiFetch('/api/sync/manifest');
  return res.json() as Promise<ManifestManga[]>;
}

export async function chapterFileUrl(chapterId: number): Promise<string> {
  return `${await baseUrl()}/api/chapters/${chapterId}/file`;
}

export interface SyncMergeResult {
  received: number;
  merged: number;
}

export interface ProgressPayloadItem {
  chapterMangadexId: string;
  lastPage: number;
  isRead: boolean;
  updatedAt: string;
}

export async function pushProgress(items: ProgressPayloadItem[]): Promise<SyncMergeResult> {
  const res = await apiFetch('/api/sync/progress', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(items),
  });
  return res.json() as Promise<SyncMergeResult>;
}
