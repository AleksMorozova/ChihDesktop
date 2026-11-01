import { IntegrationsPanel } from './IntegrationsPanel';
import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import type { PlannerApi } from './bridge';
import type { RuntimeMode } from './runtime';
import { taskInputSchema } from './contracts';
import type { PlannerTask, Query, TaskInput, View } from './contracts';
import { RemindersPanel } from './RemindersPanel';
import { SettingsPanel } from './SettingsPanel';

type Page = 'Dashboard' | 'Tasks' | 'Reminders' | 'GitHub' | 'Calendar' | 'Settings';
const pages: { id: Page; label: string; icon: string }[] = [
  { id: 'Dashboard', label: 'Обзор', icon: '◈' }, { id: 'Tasks', label: 'Задачи', icon: '☷' },
  { id: 'Reminders', label: 'Напоминания', icon: '◷' }, { id: 'GitHub', label: 'GitHub', icon: '⌘' },
  { id: 'Calendar', label: 'Google Calendar', icon: '▦' },
  { id: 'Settings', label: 'Настройки', icon: '⚙' },
];
const views: { id: View; label: string }[] = [
  { id: 'AllActive', label: 'Все активные' }, { id: 'Inbox', label: 'Входящие' },
  { id: 'Today', label: 'Сегодня' }, { id: 'Upcoming', label: 'Предстоящие' }, { id: 'Completed', label: 'Завершённые' },
];
const priorityNames = { Low: 'Низкий', Normal: 'Обычный', High: 'Высокий' };
const categoryNames = { Personal: 'Личное', Study: 'Учёба', Work: 'Работа' };
const blank: TaskInput = { title: '', description: null, dueDate: null, priority: 'Normal', category: 'Personal' };
const dateLabel = (date: string) => { const [y, m, d] = date.split('-'); return `${d}.${m}.${y}`; };

export function App({ api, runtimeMode = 'webview' }: { api: PlannerApi; runtimeMode?: RuntimeMode }) {
  const [page, setPage] = useState<Page>('Dashboard');
  const [query, setQuery] = useState<Query>({ view: 'Today', sort: 'DueDate', direction: 'Ascending' });
  const [tasks, setTasks] = useState<PlannerTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [reload, setReload] = useState(0);
  const [editor, setEditor] = useState<{ task: PlannerTask | null } | null>(null);
  const [deleting, setDeleting] = useState<PlannerTask | null>(null);
  const [theme, setTheme] = useState(() => { try { return localStorage.getItem('chih.theme') ?? 'system'; } catch { return 'system'; } });
  const lastFocus = useRef<HTMLElement | null>(null);
  const isPlanner = page === 'Dashboard' || page === 'Tasks';
  const unavailable = runtimeMode === 'browser-unavailable';

  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const apply = () => { document.documentElement.dataset.theme = theme === 'system' ? (media.matches ? 'dark' : 'light') : theme; };
    apply(); media.addEventListener('change', apply);
    try { localStorage.setItem('chih.theme', theme); } catch { /* Storage is optional; no task data is cached. */ }
    return () => media.removeEventListener('change', apply);
  }, [theme]);

  useEffect(() => {
    if (unavailable) { setLoading(false); return; }
    if (!isPlanner) return;
    let current = true;
    setLoading(true); setError('');
    api.request('planner.list', { query }).then(result => { if (current) setTasks(result); })
      .catch(cause => { if (current) setError(message(cause)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [api, query, reload, isPlanner, unavailable]);

  function navigate(next: Page) {
    setPage(next); setNotice(''); setError('');
    if (next === 'Dashboard') setQuery(q => ({ ...q, view: 'Today' }));
    if (next === 'Tasks') setQuery(q => ({ ...q, view: 'AllActive' }));
  }
  function openEditor(task: PlannerTask | null) { lastFocus.current = document.activeElement as HTMLElement; setEditor({ task }); }
  function closeModal() { setEditor(null); setDeleting(null); setError(''); requestAnimationFrame(() => (lastFocus.current?.isConnected ? lastFocus.current : document.getElementById('main'))?.focus()); }
  async function mutate(action: () => Promise<unknown>, success: string) {
    setBusy(true); setError(''); setNotice('');
    try { await action(); closeModal(); setNotice(runtimeMode === 'browser-demo' ? 'ДЕМО · ' + success + ' Только в памяти браузера.' : success); setReload(n => n + 1); }
    catch (cause) { setError(message(cause)); }
    finally { setBusy(false); }
  }
  const heading = pages.find(item => item.id === page)!.label;
  return <div className="app-shell">
    <a className="skip-link" href="#main">К содержимому</a>
    <aside className="sidebar" aria-label="Основная навигация" inert={!!(editor || deleting)}>
      <div className="brand"><span className="brand-mark">C<span>•</span></span><div>chih<span className="brand-caption">ЛИЧНЫЙ ПЛАНИРОВЩИК</span></div></div>
      <div className="workspace"><span className="workspace-avatar">Ч</span><div>Моё пространство<small>Всё под контролем Чиха</small></div><span className="workspace-dot" /></div>
      <div className="nav-label">ПРОСТРАНСТВО</div>
      <nav>{pages.map(item => <button key={item.id} className={`nav-item ${page === item.id ? 'selected' : ''}`} aria-current={page === item.id ? 'page' : undefined} disabled={busy} onClick={() => navigate(item.id)}><span aria-hidden="true">{item.icon}</span>{item.label}{item.id === 'GitHub' && <small>СКОРО</small>}</button>)}</nav>
      <div className="sidebar-bottom"><div className="chih-note"><span className="note-eyebrow">ЗАМЕТКА ОТ ЧИХА</span><p>Ты составляй планы.<br />Я пока полежу рядом.</p><span>Старший по продуктивности</span></div><div className="local-status"><i /> {runtimeMode === 'browser-demo' ? 'Демо · только память' : unavailable ? 'Нет нативного подключения' : 'Только на этом компьютере'}</div><span className="version">ChihDesktop · v0.4.0</span></div>
    </aside>
    <div className="main-column" inert={!!(editor || deleting)}>
      <header className="topbar"><div>Моё пространство <span>/</span> <strong>{heading}</strong></div><div className="topbar-right"><span className="timezone">Часовой пояс из настроек</span><label className="theme-label"><span className="sr-only">Тема</span><select aria-label="Тема" value={theme} onChange={e => setTheme(e.target.value)}><option value="system">◐ Системная</option><option value="light">☀ Светлая</option><option value="dark">☾ Тёмная</option></select></label></div></header>
      <main id="main" tabIndex={-1}>
        {runtimeMode === 'browser-demo' && <div className="runtime-banner demo-banner" role="note"><strong>РЕЖИМ РАЗРАБОТКИ · ДЕМО-ДАННЫЕ</strong><span>Примеры только в памяти браузера. SQLite и ваши задачи недоступны. Изменения исчезнут после обновления страницы. Реальный планировщик: меню кота → «Планировщик Чиха…».</span></div>}
        {unavailable && <div className="runtime-banner" role="note"><strong>ОТКРЫТО В ОБЫЧНОМ БРАУЗЕРЕ</strong><span>Это собранный интерфейс без нативного подключения. Для работы с вашими задачами откройте меню кота → «Планировщик Чиха…». Для демо-разработки запустите npm run dev.</span></div>}
        <div className="page-heading"><div><span className="eyebrow">ТВОЙ ДЕНЬ. В ТВОЁМ ТЕМПЕ.</span><h1>{page === 'Dashboard' ? 'Меньше суеты. Больше дела.' : heading}</h1><p>{page === 'Dashboard' ? 'Начнём с главного. Остальное подождёт.' : page === 'Tasks' ? 'Маленькие шаги тоже считаются. Чих проверил.' : 'Одно пространство для спокойного рабочего дня.'}</p></div>{isPlanner && <button className="primary" disabled={busy || unavailable} onClick={() => openEditor(null)}><span aria-hidden="true">＋</span> Новая задача</button>}</div>
        {notice && <div className="notice" role="status">✓ {notice}</div>}
        {error && !editor && !deleting && <ErrorNotice error={error} retry={() => setReload(n => n + 1)} />}
        {page === 'Dashboard' && <section className="dashboard-intro" aria-label="План на сегодня"><div><span className="pill">СЕГОДНЯ В ФОКУСЕ</span><h2>Сначала важное.<br />Потом погладить Чиха.</h2><p>Здесь задачи на сегодня и всё, что ждёт с прошлых дней.</p><button className="text-button" disabled={busy} onClick={() => navigate('Tasks')}>Все задачи <span aria-hidden="true">↗</span></button></div><div className="focus-number"><span>{loading || error || unavailable ? '—' : tasks.length.toString().padStart(2, '0')}</span><small>задач в этом списке</small><span className="orbit" aria-hidden="true">✦</span></div></section>}
        {isPlanner ? <section className="task-panel" aria-labelledby="tasks-title"><div className="panel-title"><div><h2 id="tasks-title">{page === 'Dashboard' ? 'План на сегодня' : 'Мои задачи'}</h2><span>{runtimeMode === 'browser-demo' ? 'Демо-данные · изменения не сохраняются' : unavailable ? 'Подключение доступно только в приложении' : query.view === 'Today' ? 'Сегодня и просроченные' : 'Сохраняется локально, без облака'}</span></div><button className="subtle" disabled={busy || loading || unavailable} onClick={() => setReload(n => n + 1)} aria-label="Обновить список">↻ Обновить</button></div>
          {page === 'Tasks' && <div className="filters" role="group" aria-label="Фильтр задач">{views.map(view => <button aria-pressed={query.view === view.id} disabled={busy || unavailable} className={query.view === view.id ? 'active' : ''} key={view.id} onClick={() => setQuery(q => ({ ...q, view: view.id }))}>{view.label}</button>)}</div>}
          <div className="list-toolbar"><span>{unavailable ? 'Нет подключения' : loading ? 'Загрузка…' : `${tasks.length} в списке`}</span><div><label>Сортировка <select aria-label="Сортировка" disabled={busy || unavailable} value={query.sort} onChange={e => setQuery(q => ({ ...q, sort: e.target.value as Query['sort'] }))}><option value="DueDate">По сроку</option><option value="Priority">По приоритету</option><option value="CreatedAt">По созданию</option></select></label><button className="sort-direction" disabled={busy || unavailable} aria-label="Направление сортировки" title={query.direction === 'Ascending' ? 'По возрастанию' : 'По убыванию'} onClick={() => setQuery(q => ({ ...q, direction: q.direction === 'Ascending' ? 'Descending' : 'Ascending' }))}>{query.direction === 'Ascending' ? '↑' : '↓'}</button></div></div>
          <div className="task-list" aria-busy={loading || busy}>{unavailable ? <div className="empty-state"><span aria-hidden="true">↗</span><h3>Откройте планировщик у Чиха</h3><p>В браузере нет доступа к нативным службам. Ваши задачи доступны в окне приложения.</p></div> : loading ? <div className="loading" role="status"><span className="spinner" /> Чих сверяет список…</div> : error ? <div className="empty-state"><span aria-hidden="true">!</span><h3>Список пока недоступен</h3><p>Проверьте сообщение выше и попробуйте обновить.</p></div> : tasks.length === 0 ? <div className="empty-state"><span aria-hidden="true">✓</span><h3>{query.view === 'Completed' ? 'Здесь будут маленькие победы' : 'Свободное место для хороших планов'}</h3><p>{query.view === 'Today' ? 'На сегодня задач нет. Чих одобряет передышку.' : 'Добавьте задачу или выберите другой список.'}</p><button className="subtle" disabled={busy} onClick={() => openEditor(null)}>＋ Добавить задачу</button></div> : tasks.map(task => <article className={`task-row ${task.status === 'Completed' ? 'completed' : ''}`} key={task.id}>
            <button className="complete-button" disabled={busy} aria-label={`${task.status === 'Active' ? 'Завершить' : 'Вернуть'}: ${task.title}`} onClick={() => mutate(() => task.status === 'Active' ? api.request('planner.complete', { id: task.id }) : api.request('planner.reopen', { id: task.id }), task.status === 'Active' ? 'Задача завершена. Чих впечатлён.' : 'Задача снова в работе.')}><span aria-hidden="true">{task.status === 'Completed' ? '✓' : ''}</span></button>
            <div className="task-content"><button className="task-title" disabled={busy} onClick={() => openEditor(task)}>{task.title}</button>{task.description && <p>{task.description}</p>}<div className="task-meta"><span className="category">{categoryNames[task.category]}</span>{task.dueDate && <time dateTime={task.dueDate}>◷ {dateLabel(task.dueDate)}</time>}{task.remindAtUtc && <span>🔔 {task.reminderStatus ?? "Pending"}</span>}<span className={`priority priority-${task.priority}`}>● {priorityNames[task.priority]}</span></div></div>
            <div className="row-actions">{task.remindAtUtc && task.status === "Active" && <button disabled={busy} aria-label={`Отложить: ${task.title}`} onClick={() => mutate(() => api.request("planner.reminder", { id: task.id, expectedRevision: task.revision, action: "snooze" }), "Напоминание отложено на 10 минут.")}>🔔 +10</button>}<button disabled={busy} aria-label={`Редактировать: ${task.title}`} onClick={() => openEditor(task)}>✎</button><button disabled={busy} aria-label={`Удалить: ${task.title}`} onClick={() => { lastFocus.current = document.activeElement as HTMLElement; setDeleting(task); setError(''); }}>×</button></div>
          </article>)}</div>
          <footer className="panel-footer"><span><i /> {runtimeMode === 'browser-demo' ? 'ДЕМО · данные только в памяти' : unavailable ? 'SQLite не подключена' : 'Локальная база SQLite'}</span><span>Планы твои. Контроль — Чиха.</span></footer>
        </section> : page === 'Reminders' && runtimeMode === 'webview' ? <RemindersPanel api={api} /> : page === 'Settings' && runtimeMode === 'webview' ? <SettingsPanel api={api} openNative={() => void mutate(() => api.request('settings.open', {}), 'Окно настроек открыто.')} /> : (page === 'GitHub' || page === 'Calendar') ? <IntegrationsPanel /> : <section className="integration-card"><span className="integration-icon" aria-hidden="true">{pages.find(item => item.id === page)!.icon}</span><span className="pill">{page === 'Settings' ? 'ЛОКАЛЬНЫЕ НАСТРОЙКИ' : 'ИНТЕГРАЦИЯ ОЖИДАЕТСЯ'}</span><h2>{page === 'Reminders' ? 'Напоминания уже работают у Чиха' : 'Настройки пользователя'}</h2><p>{page === 'Reminders' ? 'Нажмите правой кнопкой на кота → «Напоминания — сегодня» или «Настроить напоминания…». Расписание и подтверждения остаются в прежнем окне.' : 'Имя, часовой пояс, поведение, автозапуск и повторное знакомство доступны в нативных настройках Чиха. Они работают и без WebView2. Тему этого окна можно выбрать вверху.'}</p>{page === 'Settings' && runtimeMode === 'webview' && <button className="primary" disabled={busy} onClick={() => mutate(() => api.request('settings.open', {}), 'Окно настроек открыто.')}>Открыть настройки Чиха</button>}{page === 'Settings' && <a href="https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution" target="_blank" rel="noreferrer">О Microsoft WebView2 ↗</a>}<div className="integration-footnote">Без облака. Без фоновых подключений.</div></section>}
        <div className="page-footer"><span>Сделано для спокойного дня</span><span>С одобрения Чиха <span aria-hidden="true">✦</span></span></div>
      </main>
    </div>
    {editor && <TaskEditor api={api} task={editor.task} busy={busy} error={error} onClose={closeModal} onSave={input => mutate(() => editor.task ? api.request('planner.update', { id: editor.task.id, expectedRevision: editor.task.revision, task: input }) : api.request('planner.create', { task: input }), editor.task ? 'Изменения сохранены.' : 'Задача добавлена. План одобрен.')} />}
    {deleting && <Modal title="Удалить задачу?" busy={busy} onClose={closeModal}><p className="delete-copy">«{deleting.title}» будет удалена {runtimeMode === 'browser-demo' ? 'только из памяти демо' : 'из локальной базы'}. Отменить удаление нельзя.</p>{error && <ErrorNotice error={error} />}<div className="modal-actions"><button disabled={busy} onClick={closeModal}>Отмена</button><button className="danger" disabled={busy} onClick={() => mutate(() => api.request('planner.delete', { id: deleting.id, expectedRevision: deleting.revision }), 'Задача удалена.')}>{busy ? 'Удаление…' : 'Удалить задачу'}</button></div></Modal>}
    {busy && <div className="sr-only" role="status">Сохранение изменений…</div>}
  </div>;
}
function message(cause: unknown) { return cause instanceof Error ? cause.message : 'Не удалось выполнить действие. Повторите попытку.'; }
function ErrorNotice({ error, retry }: { error: string; retry?: () => void }) { return <div className="error" role="alert"><span>{error}</span>{retry && <button onClick={retry}>Повторить</button>}</div>; }

function Modal({ title, busy, onClose, children }: { title: string; busy: boolean; onClose: () => void; children: React.ReactNode }) {
  const dialog = useRef<HTMLDivElement>(null);
  useEffect(() => { (dialog.current?.querySelector<HTMLElement>('input') ?? dialog.current?.querySelector<HTMLElement>('button'))?.focus(); }, []);
  return <div className="modal-backdrop"><div className="modal" ref={dialog} role="dialog" aria-modal="true" aria-labelledby="modal-title" onKeyDown={e => {
    if (e.key === 'Escape' && !busy) { e.stopPropagation(); onClose(); }
    if (e.key === 'Tab') {
      const controls = dialog.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), textarea:not(:disabled), select:not(:disabled), a[href]');
      if (!controls?.length) { e.preventDefault(); return; }
      const first = controls[0], last = controls[controls.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    }
  }}><div className="modal-heading"><div><span className="eyebrow">ПЛАН ПОД КОНТРОЛЕМ</span><h2 id="modal-title">{title}</h2></div><button aria-label="Закрыть" disabled={busy} onClick={onClose}>×</button></div>{children}</div></div>;
}

function TaskEditor({ api, task, busy, error, onClose, onSave }: { api: PlannerApi; task: PlannerTask | null; busy: boolean; error: string; onClose: () => void; onSave: (input: TaskInput) => void }) {
  const [input, setInput] = useState<TaskInput>(task ? { title: task.title, description: task.description, dueDate: task.dueDate, remindAtUtc: task.remindAtUtc ?? null, priority: task.priority, category: task.category } : blank);
  const [validation, setValidation] = useState('');
  const [reminderEnabled, setReminderEnabled] = useState(!!task?.remindAtUtc);
  const [localReminder, setLocalReminder] = useState('');
  const [zone, setZone] = useState('');
  const [zoneError, setZoneError] = useState('');
  const [zones, setZones] = useState<{ id: string; label: string }[]>([]);
  const [resolving, setResolving] = useState(false);
  useEffect(() => { let alive = true; api.request('settings.get', {}).then(s => { if (alive) { setZone(s.settings.timeZoneId); setZones(s.timeZones); } }).catch(() => { if (alive) setZoneError('Выбор часового пояса недоступен. Новое напоминание настраивается в приложении Чиха; существующий момент можно сохранить без изменений.'); }); return () => { alive = false; }; }, [api]);
  async function submit(e: FormEvent) {
    e.preventDefault(); if (resolving) return; setResolving(true);
    try {
      let remindAtUtc = reminderEnabled ? input.remindAtUtc ?? null : null;
      if (reminderEnabled && localReminder) remindAtUtc = await api.request('planner.resolveTime', { local: localReminder, timeZoneId: zone });
      if (reminderEnabled && !remindAtUtc) throw new Error('Выберите дату, время и часовой пояс напоминания.');
      const parsed = taskInputSchema.safeParse({ ...input, remindAtUtc });
      if (!parsed.success) throw new Error(parsed.error.issues[0].message);
      setValidation(''); onSave(parsed.data);
    } catch (cause) { setValidation(message(cause)); } finally { setResolving(false); }
  }
  return <Modal title={task ? 'Редактировать задачу' : 'Новая задача'} busy={busy || resolving} onClose={onClose}><form onSubmit={submit}><fieldset disabled={busy || resolving}>
    <label className="field">Название <span className="required">*</span><input autoComplete="off" name="title" required maxLength={200} value={input.title} onChange={e => setInput({ ...input, title: e.target.value })} placeholder="Что хочется сделать?" /></label>
    <label className="field">Описание <span className="optional">необязательно</span><textarea name="description" rows={3} maxLength={16000} value={input.description ?? ''} onChange={e => setInput({ ...input, description: e.target.value || null })} placeholder="Детали, которые помогут начать" /></label>
    <div className="field-grid"><label className="field">Срок<input name="dueDate" type="date" value={input.dueDate ?? ''} onChange={e => setInput({ ...input, dueDate: e.target.value || null })} /><small>Календарная дата · выбранный пояс</small></label><label className="field">Приоритет<select name="priority" value={input.priority} onChange={e => setInput({ ...input, priority: e.target.value as TaskInput['priority'] })}>{Object.entries(priorityNames).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label></div>
    <label className="check-field"><input type="checkbox" checked={reminderEnabled} onChange={e => setReminderEnabled(e.target.checked)} />Напомнить об этой задаче</label>
    {reminderEnabled && <>{zoneError && <p role="status">{zoneError}</p>}<label className="field">Напоминание — дата и время<input type="datetime-local" value={localReminder} onChange={e => setLocalReminder(e.target.value)} /><small>{input.remindAtUtc ? `Сохранённый момент: ${input.remindAtUtc}. Оставьте поле пустым, чтобы сохранить его.` : 'Не зависит от срока задачи.'}</small></label><label className="field">Часовой пояс напоминания<select value={zone} onChange={e => setZone(e.target.value)}><option value="">Выберите пояс</option>{zones.map(z => <option key={z.id} value={z.id}>{z.label}</option>)}</select></label><p className="admin-help">Статус: {task?.reminderStatus ?? 'Pending'}. Повторяющееся или пропущенное время перевода часов нужно заменить. Сохранённый момент не меняется при смене пояса.</p></>}
    <label className="field">Категория<select name="category" value={input.category} onChange={e => setInput({ ...input, category: e.target.value as TaskInput['category'] })}>{Object.entries(categoryNames).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
  </fieldset>{(validation || error) && <ErrorNotice error={validation || error} />}<div className="modal-actions"><button type="button" disabled={busy} onClick={onClose}>Отмена</button><button type="submit" className="primary" disabled={busy || resolving}>{busy || resolving ? 'Сохранение…' : 'Сохранить задачу'}</button></div></form></Modal>;
}
