import { BridgeClient } from './bridge';
import type { PlannerApi, Transport } from './bridge';

export type RuntimeMode = 'webview' | 'browser-demo' | 'browser-unavailable';
export interface AdminRuntime { mode: RuntimeMode; api: PlannerApi; dispose(): void }

export function isNativeTransport(value: unknown): value is Transport {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<Transport>;
  return typeof candidate.postMessage === 'function' && typeof candidate.addEventListener === 'function' && typeof candidate.removeEventListener === 'function';
}

export async function createAdminRuntime(transport: unknown = window.chrome?.webview, allowDemo = import.meta.env.DEV): Promise<AdminRuntime> {
  if (isNativeTransport(transport)) {
    const api = new BridgeClient(transport);
    return { mode: 'webview', api, dispose: () => api.dispose() };
  }
  // The compile-time DEV guard removes this module and its sample data from production.
  // A failing native bridge NEVER switches to a mock adapter.
  if (import.meta.env.DEV && allowDemo) {
    const { BrowserDemoAdapter } = await import('./browserDemo');
    const api = new BrowserDemoAdapter();
    return { mode: 'browser-demo', api, dispose: () => api.dispose() };
  }
  return {
    mode: 'browser-unavailable',
    api: { request: () => Promise.reject(new Error('Откройте «Планировщик Чиха…» из меню кота: браузер не имеет доступа к SQLite.')) },
    dispose() {},
  };
}
