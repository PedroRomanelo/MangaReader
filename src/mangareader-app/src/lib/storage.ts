import { Filesystem, Directory } from '@capacitor/filesystem';
import { chapterFileUrl } from './api';

const ROOT = 'mangareader';

function chapterPath(mangaId: number, chapterId: number): string {
  return `${ROOT}/${mangaId}/${chapterId}.cbz`;
}

// Diretório app-privado externo no Android: /storage/emulated/0/Android/data/<pkg>/files
// Não precisa de permissão em Android 10+, some se o app é desinstalado.
// TODO: dar opção de escolher pasta no cartão físico via SAF picker.
const DIR = Directory.External;

function blobToBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => {
      const s = reader.result as string;
      const idx = s.indexOf(',');
      resolve(idx >= 0 ? s.slice(idx + 1) : s);
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(blob);
  });
}

export interface DownloadedChapter {
  path: string;
  size: number;
}

export async function downloadChapter(
  mangaId: number,
  chapterId: number,
): Promise<DownloadedChapter> {
  const url = await chapterFileUrl(chapterId);
  const path = chapterPath(mangaId, chapterId);

  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status} baixando cap ${chapterId}`);

  const blob = await res.blob();
  const base64 = await blobToBase64(blob);

  await Filesystem.writeFile({
    path,
    data: base64,
    directory: DIR,
    recursive: true,
  });

  return { path, size: blob.size };
}

export async function isDownloaded(mangaId: number, chapterId: number): Promise<boolean> {
  try {
    await Filesystem.stat({ path: chapterPath(mangaId, chapterId), directory: DIR });
    return true;
  } catch {
    return false;
  }
}

export async function localChapterSize(
  mangaId: number,
  chapterId: number,
): Promise<number | null> {
  try {
    const stat = await Filesystem.stat({
      path: chapterPath(mangaId, chapterId),
      directory: DIR,
    });
    return stat.size;
  } catch {
    return null;
  }
}

export async function deleteChapter(mangaId: number, chapterId: number): Promise<void> {
  try {
    await Filesystem.deleteFile({
      path: chapterPath(mangaId, chapterId),
      directory: DIR,
    });
  } catch {
    // já não existe: ok
  }
}
