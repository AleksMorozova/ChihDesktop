import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import type { PlannerApi } from '../bridge';
import type { Operation, PlannerTask } from '../contracts';

const initial: PlannerTask = { id: 'fe69b276-b987-46dd-8dbf-75e7d0d6fcd2', title: 'Synthetic task', description: null,
  status: 'Active', priority: 'Normal', category: 'Work', dueDate: '2026-10-31', createdAtUtc: '2026-10-31T10:00:00+00:00', updatedAtUtc: '2026-10-31T10:00:00+00:00', completedAtUtc: null, revision: 1 };
function fixture(rows: PlannerTask[] = [initial]) {
  let tasks = rows.map(task => ({ ...task }));
  // Test-only service stand-in. The production frontend never filters or stores task data.
  const request = vi.fn(async (operation: Operation, payload: any) => {
    if (operation === 'planner.list') return tasks;
    if (operation === 'planner.create') { const added = { ...initial, ...payload.task, id: crypto.randomUUID() }; tasks = [...tasks, added]; return added; }
    const found = tasks.find(task => task.id === payload.id)!;
    if (operation === 'planner.update') Object.assign(found, payload.task, { revision: found.revision + 1 });
    if (operation === 'planner.complete') Object.assign(found, { status: 'Completed', completedAtUtc: initial.createdAtUtc });
    if (operation === 'planner.reopen') Object.assign(found, { status: 'Active', completedAtUtc: null });
    if (operation === 'planner.delete') { tasks = tasks.filter(task => task.id !== payload.id); return true; }
    return found;
  });
  return { api: { request } as unknown as PlannerApi, request };
}
describe('planner interface', () => {
  it('opens the native user settings through the specific allowlisted bridge entry', async () => {
    const { api, request } = fixture(); const user = userEvent.setup(); render(<App api={api} />);
    await screen.findByRole('button', { name: 'Synthetic task' });
    await user.click(screen.getByRole('button', { name: 'Настройки' }));
    request.mockResolvedValueOnce(true);
    await user.click(screen.getByRole('button', { name: 'Открыть настройки Чиха' }));
    await screen.findByText('✓ Окно настроек открыто.');
    expect(request).toHaveBeenCalledWith('settings.open', {});
  });
  it('treats a production browser without WebView2 as an expected mode, without querying a backend', async () => {
    const { api, request } = fixture();
    render(<App api={api} runtimeMode="browser-unavailable" />);
    expect(await screen.findByRole('heading', { name: 'Откройте планировщик у Чиха' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Новая задача' })).toBeDisabled();
    expect(request).not.toHaveBeenCalled();
  });

  it('passes filtering and sorting to C# instead of reproducing date logic', async () => {
    const { api, request } = fixture(); const user = userEvent.setup(); render(<App api={api} />);
    await screen.findByRole('button', { name: 'Synthetic task' });
    expect(request).toHaveBeenLastCalledWith('planner.list', { query: { view: 'Today', sort: 'DueDate', direction: 'Ascending' } });
    await user.click(screen.getByRole('button', { name: 'Задачи' }));
    for (const [label, view] of [['Входящие', 'Inbox'], ['Сегодня', 'Today'], ['Предстоящие', 'Upcoming'], ['Завершённые', 'Completed']]) {
      await user.click(screen.getByRole('button', { name: label }));
      await waitFor(() => expect(request).toHaveBeenLastCalledWith('planner.list', { query: expect.objectContaining({ view }) }));
    }
    await user.selectOptions(screen.getByLabelText('Сортировка'), 'Priority');
    await user.click(screen.getByLabelText('Направление сортировки'));
    await waitFor(() => expect(request).toHaveBeenLastCalledWith('planner.list', { query: expect.objectContaining({ sort: 'Priority', direction: 'Descending' }) }));
    await user.selectOptions(screen.getByLabelText('Сортировка'), 'CreatedAt');
    await waitFor(() => expect(request).toHaveBeenLastCalledWith('planner.list', { query: expect.objectContaining({ sort: 'CreatedAt' }) }));
  });
  it('creates and edits all task fields with the expected revision', async () => {
    const { api, request } = fixture([]); const user = userEvent.setup(); render(<App api={api} />);
    await screen.findByText('Свободное место для хороших планов');
    await user.click(screen.getByRole('button', { name: 'Новая задача' }));
    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText(/Название/), 'New synthetic task');
    await user.type(within(dialog).getByLabelText(/Описание/), 'Synthetic description');
    await user.type(within(dialog).getByLabelText(/^Срок/), '2026-12-01');
    await user.selectOptions(within(dialog).getByLabelText('Приоритет'), 'High');
    await user.selectOptions(within(dialog).getByLabelText('Категория'), 'Study');
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить задачу' }));
    await screen.findByRole('button', { name: 'New synthetic task' });
    expect(request).toHaveBeenCalledWith('planner.create', { task: { title: 'New synthetic task', description: 'Synthetic description', dueDate: '2026-12-01', priority: 'High', category: 'Study' } });
    await user.click(screen.getByRole('button', { name: 'Редактировать: New synthetic task' }));
    const title = within(screen.getByRole('dialog')).getByLabelText(/Название/);
    await user.clear(title); await user.type(title, 'Edited synthetic task');
    await user.click(screen.getByRole('button', { name: 'Сохранить задачу' }));
    await screen.findByRole('button', { name: 'Edited synthetic task' });
    expect(request).toHaveBeenCalledWith('planner.update', expect.objectContaining({ expectedRevision: 1, task: expect.objectContaining({ title: 'Edited synthetic task' }) }));
  });
  it('completes, reopens, and confirms deletion explicitly', async () => {
    const { api, request } = fixture(); const user = userEvent.setup(); render(<App api={api} />);
    await user.click(await screen.findByRole('button', { name: 'Завершить: Synthetic task' }));
    await user.click(await screen.findByRole('button', { name: 'Вернуть: Synthetic task' }));
    await screen.findByRole('button', { name: 'Завершить: Synthetic task' });
    await user.click(screen.getByRole('button', { name: 'Удалить: Synthetic task' }));
    expect(request).not.toHaveBeenCalledWith('planner.delete', expect.anything());
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Отмена' }));
    await user.click(screen.getByRole('button', { name: 'Удалить: Synthetic task' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить задачу' }));
    await screen.findByText('Свободное место для хороших планов');
    expect(request).toHaveBeenCalledWith('planner.delete', { id: initial.id, expectedRevision: 1 });
  });
  it('shows loading and actionable read errors with retry', async () => {
    let reject!: (error: Error) => void;
    const request = vi.fn(() => new Promise((_, failure) => { reject = failure; }));
    render(<App api={{ request } as unknown as PlannerApi} />);
    expect(screen.getByText('Чих сверяет список…')).toBeInTheDocument();
    reject(new Error('Check disk permissions'));
    expect(await screen.findByRole('alert')).toHaveTextContent('Check disk permissions');
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }));
    await waitFor(() => expect(request).toHaveBeenCalledTimes(2));
  });
  it('keeps an unsuccessful mutation open without displaying success', async () => {
    const { api, request } = fixture(); const user = userEvent.setup(); render(<App api={api} />);
    await user.click(await screen.findByRole('button', { name: 'Редактировать: Synthetic task' }));
    request.mockRejectedValueOnce(new Error('Write failed. Check disk.'));
    await user.click(screen.getByRole('button', { name: 'Сохранить задачу' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Write failed. Check disk.');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.queryByText('Изменения сохранены.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Сохранить задачу' })).toBeEnabled();
  });
  it('disables conflicting actions while a mutation is pending', async () => {
    const { api, request } = fixture(); const user = userEvent.setup(); render(<App api={api} />);
    await user.click(await screen.findByRole('button', { name: 'Редактировать: Synthetic task' }));
    let resolve!: (value: PlannerTask) => void;
    request.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    await user.click(screen.getByRole('button', { name: 'Сохранить задачу' }));
    expect(screen.getByRole('button', { name: 'Сохранение…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Закрыть' })).toBeDisabled();
    resolve(initial); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });
  it('uses keyboard dismissal and marks integrations as pending', async () => {
    const { api } = fixture(); const user = userEvent.setup(); render(<App api={api} />);
    await screen.findByRole('button', { name: 'Synthetic task' });
    await user.click(screen.getByRole('button', { name: 'Новая задача' }));
    await user.keyboard('{Escape}'); expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /GitHub/ }));
    expect(screen.getByText('НЕ ПОДКЛЮЧЕНО')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Напоминания/ }));
    expect(screen.getByText('ИНТЕГРАЦИЯ ОЖИДАЕТСЯ')).toBeInTheDocument();
  });
});
