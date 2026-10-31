import { describe, expect, it, vi } from 'vitest';
import { BrowserDemoAdapter } from '../browserDemo';
import { createAdminRuntime, isNativeTransport } from '../runtime';
import type { Transport } from '../bridge';

const all = { query: { view: 'AllActive' as const, sort: 'DueDate' as const, direction: 'Ascending' as const } };
describe('explicit runtime selection', () => {
  it('creates a labeled demo only for development without a native bridge', async () => {
    const runtime = await createAdminRuntime(null);
    expect(runtime.mode).toBe('browser-demo');
    expect((await runtime.api.request('planner.list', all)).every(task => task.title.startsWith('ДЕМО ·'))).toBe(true);
    runtime.dispose();
  });
  it('does not create fake data when the development gate is off', async () => {
    const runtime = await createAdminRuntime(null, false);
    expect(runtime.mode).toBe('browser-unavailable');
    await expect(runtime.api.request('planner.list', all)).rejects.toThrow('из меню кота');
    runtime.dispose();
  });
  it('uses native transport even in development and never falls back after an error', async () => {
    let receive!: (event: { data: unknown }) => void;
    const transport: Transport = {
      addEventListener: (_, listener) => { receive = listener; }, removeEventListener: vi.fn(),
      postMessage(message) {
        const { requestId, operation } = message as { requestId: string; operation: string };
        receive({ data: { protocolVersion: 1, requestId, operation, success: false, data: null, error: { code: 'PersistenceUnavailable', message: 'Synthetic native failure' } } });
      },
    };
    const runtime = await createAdminRuntime(transport);
    expect(runtime.mode).toBe('webview');
    await expect(runtime.api.request('planner.list', all)).rejects.toThrow('Synthetic native failure');
    expect(runtime.mode).toBe('webview'); runtime.dispose();
  });
  it('checks the native transport shape rather than the existence of window.chrome', () => {
    expect(isNativeTransport({})).toBe(false);
    expect(isNativeTransport({ postMessage: 'not a function' })).toBe(false);
    expect(isNativeTransport(null)).toBe(false);
  });
});

describe('isolated typed browser demo', () => {
  it('uses detached sample DTOs and never persists between adapter instances', async () => {
    const adapter = new BrowserDemoAdapter();
    const task = await adapter.request('planner.create', { task: { title: 'Synthetic transient task', description: null, dueDate: null, category: 'Study', priority: 'Normal' } });
    task.title = 'modified returned DTO';
    expect((await adapter.request('planner.get', { id: task.id })).title).toBe('Synthetic transient task');
    const edited = await adapter.request('planner.update', { id: task.id, expectedRevision: task.revision, task: { title: 'Synthetic edited task', description: 'Synthetic details', dueDate: null, category: 'Study', priority: 'High' } });
    expect(edited.title).toBe('Synthetic edited task');
    expect(edited.revision).toBe(2);
    await adapter.request('planner.delete', { id: task.id, expectedRevision: edited.revision });
    await expect(adapter.request('planner.get', { id: task.id })).rejects.toThrow('не найдена');
    adapter.dispose();
    const fresh = new BrowserDemoAdapter();
    expect((await fresh.request('planner.list', all)).some(row => row.title === 'Synthetic transient task')).toBe(false);
    fresh.dispose();
  });
  it('provides all views and honors idempotent completion/reopening for visual tests', async () => {
    const adapter = new BrowserDemoAdapter(() => new Date('2026-10-31T12:00:00Z'));
    const list = async (view: 'Inbox' | 'Today' | 'Upcoming' | 'Completed') => adapter.request('planner.list', { query: { ...all.query, view } });
    expect(await list('Inbox')).toHaveLength(1);
    expect(await list('Today')).toHaveLength(2);
    expect(await list('Upcoming')).toHaveLength(1);
    expect(await list('Completed')).toHaveLength(1);
    const task = (await list('Today'))[0];
    const done = await adapter.request('planner.complete', { id: task.id });
    expect(await adapter.request('planner.complete', { id: task.id })).toEqual(done);
    const reopened = await adapter.request('planner.reopen', { id: task.id });
    expect(reopened.completedAtUtc).toBeNull();
    await expect(adapter.request('planner.delete', { id: task.id, expectedRevision: 1 })).rejects.toThrow('изменена');
    adapter.dispose();
    await expect(adapter.request('planner.list', all)).rejects.toThrow('закрыто');
  });
});
