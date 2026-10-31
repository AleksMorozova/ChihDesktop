import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  outputDir: '../../artifacts/phase4-admin-fix/browser-tests',
  reporter: 'list',
  use: { channel: 'chrome', headless: true, viewport: { width: 1280, height: 900 }, screenshot: 'only-on-failure' },
  webServer: [
    { command: 'npm run dev -- --port 5187 --strictPort', url: 'http://127.0.0.1:5187', reuseExistingServer: false },
    { command: 'npm exec vite preview -- --host 127.0.0.1 --port 5188 --strictPort', url: 'http://127.0.0.1:5188', reuseExistingServer: false },
  ],
});
