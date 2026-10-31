import { describe, expect, it } from 'vitest';
import { requestSchema, responseSchema, taskInputSchema, taskSchema } from '../contracts';

export const task = { id: 'fe69b276-b987-46dd-8dbf-75e7d0d6fcd2', title: 'Synthetic task', description: null,
  status: 'Active' as const, priority: 'Normal' as const, category: 'Work' as const, dueDate: '2026-10-31',
  createdAtUtc: '2026-10-31T10:00:00+00:00', updatedAtUtc: '2026-10-31T10:00:00+00:00', completedAtUtc: null, revision: 1 };
describe('versioned contracts', () => {
  it('accepts typed DTOs and retains calendar dates verbatim', () => { expect(taskSchema.parse(task).dueDate).toBe('2026-10-31'); });
  it.each(['2026-02-30', '31.10.2026', '2026-10-31T00:00:00Z'])('rejects invalid DateOnly %s', dueDate => { expect(taskSchema.safeParse({ ...task, dueDate }).success).toBe(false); });
  it('rejects EF/internal fields, numeric enums and missing fields', () => {
    expect(taskSchema.safeParse({ ...task, tracking: true }).success).toBe(false);
    expect(taskSchema.safeParse({ ...task, priority: 1 }).success).toBe(false);
    expect(taskInputSchema.safeParse({ title: '' }).success).toBe(false);
  });
  it('rejects unknown operations, mismatched version and untyped payloads', () => {
    const base = { protocolVersion: 1, requestId: task.id, operation: 'planner.get', payload: { id: task.id } };
    expect(requestSchema.safeParse(base).success).toBe(true);
    expect(requestSchema.safeParse({ ...base, operation: 'filesystem.read' }).success).toBe(false);
    expect(requestSchema.safeParse({ ...base, protocolVersion: 2 }).success).toBe(false);
    expect(requestSchema.safeParse({ ...base, payload: { sql: 'anything' } }).success).toBe(false);
  });
  it('validates explicit error/success envelopes', () => {
    expect(responseSchema.safeParse({ protocolVersion: 1, requestId: task.id, operation: 'planner.get', success: false, data: null, error: { code: 'NotFound', message: 'Не найдено' } }).success).toBe(true);
    expect(responseSchema.safeParse({ protocolVersion: 1, requestId: task.id, operation: 'planner.get', success: true, data: task, error: { code: 'x', message: 'x' } }).success).toBe(false);
  });
});
