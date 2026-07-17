import { writable } from 'svelte/store';

export type Route =
  | { name: 'library' }
  | { name: 'settings' }
  | { name: 'manga'; mangaId: number }
  | { name: 'reader'; mangaId: number; chapterId: number; chapterMangadexId: string };

export const route = writable<Route>({ name: 'library' });

export function go(next: Route): void {
  route.set(next);
}
