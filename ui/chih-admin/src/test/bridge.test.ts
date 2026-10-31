import { describe, expect, it, vi } from 'vitest';
import { BridgeClient } from '../bridge';
import type { Transport } from '../bridge';

class FakeTransport implements Transport {
  messages: { requestId: string; operation: string }[] = [];
  listener?: (event: { data: unknown }) => void;
  postMessage(message: unknown) { this.messages.push(message as typeof this.messages[number]); }
  addEventListener(_: 'message', listener: (event: { data: unknown }) => void) { this.listener = listener; }
  removeEventListener() { this.listener = undefined; }
  reply(data: unknown, extra = {}) { const request = this.messages.at(-1)!; this.listener?.({ data: { protocolVersion: 1, requestId: request.requestId, operation: request.operation, success: true, data, error: null, ...extra } }); }
}
const query = { query: { view: 'Today' as const, sort: 'DueDate' as const, direction: 'Ascending' as const } };
describe('bridge lifecycle', () => {
  it('correlates and validates replies, ignoring duplicate replies', async () => {
    const transport = new FakeTransport(); const client = new BridgeClient(transport);
    const pending = client.request('planner.list', query); transport.reply([]);
    await expect(pending).resolves.toEqual([]); transport.reply([]); client.dispose();
  });
  it('rejects malformed DTO data and protocol mismatches', async () => {
    const transport = new FakeTransport(); const client = new BridgeClient(transport);
    const pending = client.request('planner.list', query); transport.reply([{ title: 'incomplete' }]);
    await expect(pending).rejects.toThrow('Некорректные');
    const second = client.request('planner.list', query); transport.reply([], { protocolVersion: 2 });
    await expect(second).rejects.toThrow('Несовместимый'); client.dispose();
  });
  it('surfaces host errors and never silently retries mutations', async () => {
    const transport = new FakeTransport(); const client = new BridgeClient(transport);
    const pending = client.request('planner.list', query); transport.reply(null, { success: false, error: { code: 'PersistenceUnavailable', message: 'Disk unavailable' } });
    await expect(pending).rejects.toThrow('Disk unavailable'); expect(transport.messages).toHaveLength(1); client.dispose();
  });
  it('times out with unknown-outcome guidance', async () => {
    vi.useFakeTimers();
    try {
      const client = new BridgeClient(new FakeTransport(), 100);
      const assertion = expect(client.request('planner.list', query)).rejects.toThrow('Результат действия неизвестен');
      await vi.advanceTimersByTimeAsync(101); await assertion; client.dispose();
    } finally { vi.useRealTimers(); }
  });
  it('bounds requests and rejects pending work on disposal', async () => {
    const transport = new FakeTransport(); const client = new BridgeClient(transport);
    const pending = Array.from({ length: 8 }, () => client.request('planner.list', query).catch(error => error));
    await expect(client.request('planner.list', query)).rejects.toThrow('Дождитесь');
    client.dispose(); expect(await Promise.all(pending)).toHaveLength(8); expect(transport.listener).toBeUndefined();
    await expect(client.request('planner.list', query)).rejects.toThrow('Окно закрыто');
  });
  it('does not substitute a mock database in a regular browser', async () => {
    const client = new BridgeClient(); await expect(client.request('planner.list', query)).rejects.toThrow('из меню Чиха'); client.dispose();
  });
});
