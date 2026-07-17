import { Preferences } from '@capacitor/preferences';

const SERVER_URL_KEY = 'serverUrl';

function normalize(url: string): string {
  return url.trim().replace(/\/+$/, '');
}

export async function getServerUrl(): Promise<string | null> {
  const { value } = await Preferences.get({ key: SERVER_URL_KEY });
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

export async function setServerUrl(url: string): Promise<void> {
  await Preferences.set({ key: SERVER_URL_KEY, value: normalize(url) });
}

export async function clearServerUrl(): Promise<void> {
  await Preferences.remove({ key: SERVER_URL_KEY });
}
