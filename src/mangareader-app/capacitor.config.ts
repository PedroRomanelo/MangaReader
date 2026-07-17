import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.romanelo.mangareader',
  appName: 'MangaReader',
  webDir: 'dist',
  android: {
    // Backend LAN roda em http://; sem isso o WebView bloqueia cleartext.
    allowMixedContent: true,
  },
};

export default config;
