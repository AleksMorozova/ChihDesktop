import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { PlannerApi } from '../bridge';
import type { Operation, Reminder, Reminders, Settings } from '../contracts';
import { payloadSchemas, settingsSchema } from '../contracts';
import { RemindersPanel } from '../RemindersPanel';
import { SettingsPanel } from '../SettingsPanel';

const version = 'A'.repeat(64);
const reminder: Reminder = { id: 'synthetic-definition', version, timeZoneId: 'UTC', definition: { title: 'Synthetic reminder', time: '10:00', recurrence: 'Daily', intervalMinutes: 2, weeklyDay: 'Monday', enabled: false, bypassQuietHours: false } };
const settings: Settings = { version, canWrite: true, error: null, timeZones: [{ id: 'UTC', label: 'UTC' }], settings: {
  ownerDisplayName: '', timeZoneId: 'UTC', launchAtSignIn: false, startPetOnly: true, petTopmost: true, reminderNotificationsEnabled: true, taskReminderNotificationsEnabled: true, taskSummarySpeechEnabled: false, showTaskTitlesInBubbles: true, taskCompletionReactionsEnabled: true,
  personalityEnabled: true, spontaneousCommentsEnabled: true, minimumCommentIntervalMinutes: 20, quietHoursEnabled: true, quietStart: '22:00', quietEnd: '08:00',
} };
function fixture() {
  let snapshot: Reminders = { definitions: [structuredClone(reminder)], occurrences: [
    { id: 'synthetic-first', reminderId: reminder.id, title: 'Synthetic first', localDate: '2026-11-02', dueAtUtc: '2026-11-02T10:00:00+00:00', acknowledgment: 'Pending', snoozedUntilUtc: null, isManualTest: false },
    { id: 'synthetic-second', reminderId: reminder.id, title: 'Synthetic second', localDate: '2026-11-02', dueAtUtc: '2026-11-02T10:00:00+00:00', acknowledgment: 'Pending', snoozedUntilUtc: null, isManualTest: false },
  ], truncated: false, baseTimeZone: 'UTC', error: null };
  let preferences = structuredClone(settings);
  const request = vi.fn(async (operation: Operation, payload: any) => {
    payloadSchemas[operation].parse(payload);
    if (operation === 'reminders.list') return structuredClone(snapshot);
    if (operation === 'settings.get') return structuredClone(preferences);
    if (operation === 'settings.save') { preferences = { ...preferences, settings: payload.settings }; return structuredClone(preferences); }
    if (operation === 'reminders.create') { const created = { ...reminder, id: 'synthetic-new', definition: payload.reminder }; snapshot.definitions.push(created); return created; }
    if (operation === 'reminders.update') { const row = snapshot.definitions.find(r => r.id === payload.id)!; row.definition = payload.reminder; return row; }
    if (operation === 'reminders.enable') snapshot.definitions.find(r => r.id === payload.id)!.definition.enabled = payload.enabled;
    if (operation === 'reminders.delete') snapshot.definitions = snapshot.definitions.filter(r => r.id !== payload.id);
    if (operation === 'reminders.acknowledge') snapshot.occurrences.find(o => o.id === payload.id)!.acknowledgment = payload.completed ? 'Completed' : 'Dismissed';
    return true;
  });
  return { api: { request } as unknown as PlannerApi, request };
}
describe('native administration panels', () => {
  it('creates, edits, enables and confirms deletion through typed commands', async () => {
    const { api, request } = fixture(); const user = userEvent.setup(); render(<RemindersPanel api={api} />);
    await screen.findByRole('heading', { name: 'Synthetic reminder' });
    await user.click(screen.getByRole('button', { name: 'Новое напоминание' }));
    expect(screen.getByRole('checkbox', { name: 'Включить напоминание' })).not.toBeChecked();
    await user.type(screen.getByRole('textbox', { name: 'Название напоминания' }), 'Synthetic new');
    await user.click(screen.getByRole('button', { name: 'Сохранить напоминание' }));
    await screen.findByRole('heading', { name: 'Synthetic new' });
    expect(request).toHaveBeenCalledWith('reminders.create', expect.objectContaining({ reminder: expect.objectContaining({ enabled: false }) }));
    await user.click(screen.getByRole('button', { name: 'Изменить напоминание: Synthetic new' }));
    fireEvent.change(screen.getByLabelText('Время напоминания'), { target: { value: '11:15' } });
    expect(screen.getByRole('combobox', { name: 'Повторение' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Сохранить напоминание' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('reminders.update', expect.objectContaining({ expectedVersion: version, reminder: expect.objectContaining({ time: '11:15' }) })));
    await user.click(screen.getByRole('checkbox', { name: 'Включено: Synthetic new' }));
    await waitFor(() => expect(screen.getByRole('checkbox', { name: 'Включено: Synthetic new' })).toBeChecked());
    await user.click(screen.getByRole('button', { name: 'Удалить напоминание: Synthetic new' }));
    expect(request).not.toHaveBeenCalledWith('reminders.delete', expect.anything());
    await user.click(screen.getByRole('button', { name: 'Подтвердить удаление' }));
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Synthetic new' })).not.toBeInTheDocument());
  });
  it('confirms each item independently; snooze and dismiss are distinct commands', async () => {
    const { api, request } = fixture(); const user = userEvent.setup(); render(<RemindersPanel api={api} />);
    await screen.findByText('Synthetic first');
    await user.click(screen.getByRole('button', { name: 'Отложить: Synthetic second' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('reminders.snooze', { id: 'synthetic-second' }));
    await user.click(screen.getByRole('button', { name: 'Выполнено: Synthetic first' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Выполнено: Synthetic first' })).not.toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Выполнено: Synthetic second' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Скрыть: Synthetic second' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('reminders.acknowledge', { id: 'synthetic-second', completed: false }));
  });
  it('closing the panel does not acknowledge anything', async () => {
    const { api, request } = fixture(); const view = render(<RemindersPanel api={api} />);
    await screen.findByText('Synthetic first'); view.unmount();
    expect(request.mock.calls.every(([operation]) => operation === 'reminders.list')).toBe(true);
  });
  it('shows write failures without optimistic durable success', async () => {
    const { api, request } = fixture(); const user = userEvent.setup(); render(<RemindersPanel api={api} />);
    await screen.findByRole('heading', { name: 'Synthetic reminder' });
    request.mockRejectedValueOnce(new Error('Synthetic write failure'));
    await user.click(screen.getByRole('checkbox', { name: 'Включено: Synthetic reminder' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Synthetic write failure');
    expect(screen.getByRole('checkbox', { name: 'Включено: Synthetic reminder' })).not.toBeChecked();
  });
  it('shows loading and actionable fetch errors', async () => {
    const api: PlannerApi = { request: vi.fn(() => Promise.reject(new Error('Synthetic unavailable'))) };
    render(<RemindersPanel api={api} />); expect(screen.getByRole('status')).toHaveTextContent('Загрузка');
    expect(await screen.findByRole('alert')).toHaveTextContent('Synthetic unavailable');
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Новое напоминание' })).toBeDisabled();
  });
  it('loads and saves real settings fields through the existing store boundary', async () => {
    const { api, request } = fixture(); const user = userEvent.setup(); const native = vi.fn(); render(<SettingsPanel api={api} openNative={native} />);
    await screen.findByRole('textbox', { name: /Как вас называть/ });
    await user.type(screen.getByRole('textbox', { name: /Как вас называть/ }), 'Synthetic <literal>');
    await user.click(screen.getByRole('checkbox', { name: 'Кот поверх окон' }));
    await user.click(screen.getByRole('button', { name: 'Сохранить настройки' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Настройки сохранены');
    expect(request).toHaveBeenCalledWith('settings.save', expect.objectContaining({ expectedVersion: version, settings: expect.objectContaining({ ownerDisplayName: 'Synthetic <literal>', petTopmost: false }) }));
    await user.click(screen.getByRole('button', { name: 'Открыть настройки Чиха' })); expect(native).toHaveBeenCalledOnce();
  });
  it('validates settings responses and rejects exposed internal fields', () => {
    expect(settingsSchema.safeParse(settings).success).toBe(true);
    expect(settingsSchema.safeParse({ ...settings, databasePath: 'not-allowed' }).success).toBe(false);
    expect(payloadSchemas['reminders.create'].safeParse({ reminder: { ...reminder.definition, time: '25:00' } }).success).toBe(false);
    expect(payloadSchemas['settings.save'].safeParse({ expectedVersion: version, settings: { ...settings.settings, schemaVersion: 99 } }).success).toBe(false);
  });
});
