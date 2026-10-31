import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig(({ command, isPreview }) => {
  const development = command === 'serve' && !isPreview;
  const nonce = crypto.randomUUID().replaceAll('-', '');
  return {
    base: './',
    html: { cspNonce: development ? nonce : undefined },
    plugins: [react(), {
      name: 'chih-development-csp',
      transformIndexHtml: {
        order: 'pre',
        handler(html) {
          // Vite injects CSS through <style> and HMR through a loopback WebSocket.
          // Production/preview retain the strict file-based CSP in index.html.
          return development ? html
            .replace("script-src 'self'", `script-src 'self' 'nonce-${nonce}'`)
            .replace("style-src 'self'", `style-src 'self' 'nonce-${nonce}'`)
            .replace("connect-src 'none'", "connect-src 'self' ws://127.0.0.1:* ws://localhost:*") : html;
        },
      },
    }],
    build: { sourcemap: false },
    test: { include: ['src/test/**/*.test.{ts,tsx}'], environment: 'jsdom', setupFiles: ['./src/test/setup.ts'], restoreMocks: true },
  };
});
