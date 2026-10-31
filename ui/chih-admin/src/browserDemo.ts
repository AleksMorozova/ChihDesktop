// Development-only UI simulation. No native services, network, storage, or reminder access.
// This module is tree-shaken out of production; Core remains the real domain authority.
import { payloadSchemas, responseSchemas } from './contracts';
import type { Operation, Payload, PlannerTask, Query, Result, TaskInput } from './contracts';
import type { PlannerApi } from './bridge';

function kyivDate(now: Date) { return new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Kyiv', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now); }
function shiftDate(date: string, days: number) { const [y, m, d] = date.split('-').map(Number); return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10); }

export class BrowserDemoAdapter implements PlannerApi {
  private tasks = new Map<string, PlannerTask>();
  private disposed = false;
  constructor(private now: () => Date = () => new Date()) {
    const today = kyivDate(now());
    const samples: TaskInput[] = [
      { title: 'ДЕМО · Разобрать заметки по проекту', description: 'Пример задачи. Эти данные не связаны с вашей базой.', dueDate: today, priority: 'High', category: 'Work' },
      { title: 'ДЕМО · Прочитать главу книги', description: 'Небольшой шаг — тоже прогресс.', dueDate: today, priority: 'Normal', category: 'Study' },
      { title: 'ДЕМО · Записать идею на выходные', description: null, dueDate: null, priority: 'Low', category: 'Personal' },
      { title: 'ДЕМО · Подготовить план недели', description: null, dueDate: shiftDate(today, 2), priority: 'Normal', category: 'Work' },
      { title: 'ДЕМО · Сделать первый маленький шаг', description: null, dueDate: shiftDate(today, -1), priority: 'Normal', category: 'Personal' },
    ];
    samples.forEach((sample, index) => {
      const task = this.create(sample);
      if (index === 4) this.tasks.set(task.id, { ...task, status: 'Completed', completedAtUtc: now().toISOString() });
    });
  }
  async request<O extends Operation>(operation: O, payload: Payload<O>): Promise<Result<O>> {
    if (this.disposed) throw new Error('Демо закрыто. Обновите страницу.');
    // Same wire schemas as WebView2; sample behavior exists only for visual development.
    payloadSchemas[operation].parse(payload);
    let result: unknown;
    switch (operation) {
      case 'settings.open': throw new Error('Настройки пользователя доступны только в приложении.');
      case 'planner.list': result = this.list(payloadSchemas['planner.list'].parse(payload).query); break;
      case 'planner.create': result = this.create(payloadSchemas['planner.create'].parse(payload).task); break;
      case 'planner.get': result = this.find(payloadSchemas['planner.get'].parse(payload).id); break;
      case 'planner.update': {
        const data = payloadSchemas['planner.update'].parse(payload); const previous = this.find(data.id);
        this.checkRevision(previous, data.expectedRevision);
        result = { ...previous, ...data.task, updatedAtUtc: this.now().toISOString(), revision: previous.revision + 1 };
        this.tasks.set(previous.id, result as PlannerTask); break;
      }
      case 'planner.complete': case 'planner.reopen': {
        const { id } = payloadSchemas['planner.get'].parse(payload); const previous = this.find(id);
        const status = operation === 'planner.complete' ? 'Completed' : 'Active';
        result = previous.status === status ? previous : { ...previous, status, completedAtUtc: status === 'Completed' ? this.now().toISOString() : null, updatedAtUtc: this.now().toISOString(), revision: previous.revision + 1 };
        this.tasks.set(id, result as PlannerTask); break;
      }
      case 'planner.delete': {
        const data = payloadSchemas['planner.delete'].parse(payload);
        this.checkRevision(this.find(data.id), data.expectedRevision); this.tasks.delete(data.id); result = true; break;
      }
    }
    return structuredClone(responseSchemas[operation].parse(result)) as Result<O>;
  }
  private create(input: TaskInput) {
    const task: PlannerTask = { ...input, id: crypto.randomUUID(), status: 'Active', createdAtUtc: this.now().toISOString(), updatedAtUtc: this.now().toISOString(), completedAtUtc: null, revision: 1 };
    this.tasks.set(task.id, task); return task;
  }
  private find(id: string) { const task = this.tasks.get(id); if (!task) throw new Error('Демо-задача не найдена. Обновите список.'); return task; }
  private checkRevision(task: PlannerTask, expected: number) { if (task.revision !== expected) throw new Error('Демо-задача изменена. Обновите список.'); }
  private list(query: Query) {
    const today = kyivDate(this.now());
    const rows = [...this.tasks.values()].filter(task => query.view === 'Completed' ? task.status === 'Completed' : task.status === 'Active' && (
      query.view === 'AllActive' || query.view === 'Inbox' && task.dueDate === null ||
      query.view === 'Today' && task.dueDate !== null && task.dueDate <= today || query.view === 'Upcoming' && task.dueDate !== null && task.dueDate > today));
    const priorities = { Low: 0, Normal: 1, High: 2 };
    return rows.sort((a, b) => {
      if (query.sort === 'DueDate' && (a.dueDate === null) !== (b.dueDate === null)) return a.dueDate === null ? 1 : -1;
      const comparison = query.sort === 'Priority' ? priorities[a.priority] - priorities[b.priority] : query.sort === 'CreatedAt' ? a.createdAtUtc.localeCompare(b.createdAtUtc) : (a.dueDate ?? '').localeCompare(b.dueDate ?? '');
      return comparison * (query.direction === 'Descending' ? -1 : 1) || a.id.localeCompare(b.id);
    });
  }
  dispose() { this.disposed = true; this.tasks.clear(); }
}
