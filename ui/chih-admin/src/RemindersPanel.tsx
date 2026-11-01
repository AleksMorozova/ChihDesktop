import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import type { PlannerApi } from './bridge';
import { reminderInputSchema, remindersSchema } from './contracts';
import type { Reminder, ReminderInput, Reminders } from './contracts';

const days: ReminderInput['weeklyDay'][] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const dayNames = ['Понедельник', 'Вторник', 'Среда', 'Четверг', 'Пятница', 'Суббота', 'Воскресенье'];
const recurrenceNames = { Daily: 'Ежедневно', Weekly: 'Еженедельно', RunningInterval: 'Интервал работы приложения' };
const blank: ReminderInput = { title: '', time: '10:00', recurrence: 'Daily', intervalMinutes: 2, weeklyDay: 'Wednesday', enabled: false, bypassQuietHours: false };
const text = (cause: unknown) => cause instanceof Error ? cause.message : 'Не удалось сохранить. Обновите список.';

export function RemindersPanel({ api }: { api: PlannerApi }) {
  const [snapshot, setSnapshot] = useState<Reminders | null>(null);
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [reload, setReload] = useState(0);
  const [error, setError] = useState(''), [notice, setNotice] = useState('');
  const [editor, setEditor] = useState<{ original: Reminder | null; input: ReminderInput } | null>(null);
  const [deleting, setDeleting] = useState<string | null>(null);
  useEffect(() => {
    let current = true; setLoading(true); setError('');
    api.request('reminders.list', {}).then(value => { if (current) setSnapshot(remindersSchema.parse(value)); })
      .catch(cause => { if (current) setError(text(cause)); }).finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [api, reload]);
  useEffect(() => {
    const refresh = () => { if (!editor && !busy && !document.hidden) setReload(n => n + 1); };
    window.addEventListener('focus', refresh); const timer = setInterval(refresh, 30000);
    return () => { window.removeEventListener('focus', refresh); clearInterval(timer); };
  }, [editor, busy]);
  async function change(action: () => Promise<unknown>, message: string) {
    setBusy(true); setError(''); setNotice('');
    try { await action(); setNotice(message); setEditor(null); setDeleting(null); setReload(n => n + 1); }
    catch (cause) { setError(text(cause)); } finally { setBusy(false); }
  }
  function save(event: FormEvent) {
    event.preventDefault(); if (!editor) return;
    const input = reminderInputSchema.safeParse(editor.input);
    if (!input.success) { setError('Проверьте название, время и интервал.'); return; }
    void change(() => editor.original ? api.request('reminders.update', { id: editor.original.id, expectedVersion: editor.original.version, reminder: input.data })
      : api.request('reminders.create', { reminder: input.data }), 'Напоминание сохранено. Остальные расписания не изменены.');
  }
  return <section className="task-panel admin-panel" aria-label="Управление напоминаниями">
    <div className="panel-title"><div><h2>Мои напоминания</h2><span>Тот же локальный JSON и нативный планировщик</span></div><div className="admin-actions">
      <button className="subtle" disabled={busy || loading} onClick={() => setReload(n => n + 1)}>Обновить напоминания</button>
      <button className="primary" disabled={busy || loading || !snapshot} onClick={() => { setEditor({ original: null, input: { ...blank } }); setError(''); }}>Новое напоминание</button></div></div>
    {error && <div className="error" role="alert">{error}<button disabled={busy} onClick={() => setReload(n => n + 1)}>Повторить</button></div>}
    {notice && <div className="notice" role="status">{notice}</div>}
    {loading && <p role="status">Загрузка напоминаний…</p>}
    {snapshot?.error && <div className="error" role="alert">{snapshot.error}</div>}
    {editor && <form className="admin-editor" onSubmit={save} aria-label={editor.original ? 'Изменение напоминания' : 'Создание напоминания'}><h3>{editor.original ? 'Редактировать напоминание' : 'Новое напоминание'}</h3><fieldset disabled={busy}>
      <label className="field">Название напоминания<input name="reminderTitle" required maxLength={200} value={editor.input.title} onChange={e => setEditor({ ...editor, input: { ...editor.input, title: e.target.value } })} /></label>
      <div className="field-grid"><label className="field">Повторение<select aria-label="Повторение" disabled={!!editor.original} value={editor.input.recurrence} onChange={e => setEditor({ ...editor, input: { ...editor.input, recurrence: e.target.value as ReminderInput['recurrence'] } })}>{Object.entries(recurrenceNames).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
        {editor.input.recurrence === 'RunningInterval' ? <label className="field">Интервал, минут<input type="number" min={1} max={1440} value={editor.input.intervalMinutes} onChange={e => setEditor({ ...editor, input: { ...editor.input, intervalMinutes: Number(e.target.value) } })} /></label>
          : <label className="field">Время напоминания<input type="time" required value={editor.input.time} onChange={e => setEditor({ ...editor, input: { ...editor.input, time: e.target.value } })} /></label>}</div>
      {editor.input.recurrence === 'Weekly' && <label className="field">День недели<select value={editor.input.weeklyDay} onChange={e => setEditor({ ...editor, input: { ...editor.input, weeklyDay: e.target.value as ReminderInput['weeklyDay'] } })}>{days.map((day, i) => <option key={day} value={day}>{dayNames[i]}</option>)}</select></label>}
      <label className="check-field"><input type="checkbox" checked={editor.input.enabled} onChange={e => setEditor({ ...editor, input: { ...editor.input, enabled: e.target.checked } })} />Включить напоминание</label>
      <label className="check-field"><input type="checkbox" checked={editor.input.bypassQuietHours} onChange={e => setEditor({ ...editor, input: { ...editor.input, bypassQuietHours: e.target.checked } })} />Разрешить в тихие часы</label>
      <p className="admin-help">Новые расписания используют выбранный в настройках пояс. Пояс и вид повторения существующего расписания сохраняются. Для их смены создайте новое и отключите старое.</p>
      <div className="admin-actions"><button type="button" onClick={() => setEditor(null)}>Отмена</button><button type="submit" className="primary">{busy ? 'Сохранение…' : 'Сохранить напоминание'}</button></div>
    </fieldset></form>}
    {!loading && !error && snapshot?.definitions.length === 0 && <div className="empty-state"><h3>Напоминаний пока нет</h3><p>Создайте своё первое. По умолчанию оно выключено.</p></div>}
    {snapshot?.definitions.map(reminder => <article className="admin-row" key={reminder.id}><div><h3>{reminder.definition.title}</h3><p>{recurrenceNames[reminder.definition.recurrence]} · {reminder.definition.recurrence === 'RunningInterval' ? `${reminder.definition.intervalMinutes} мин` : reminder.definition.time}{reminder.definition.recurrence === 'Weekly' ? ` · ${dayNames[days.indexOf(reminder.definition.weeklyDay)]}` : ''} · {reminder.timeZoneId}</p></div>
      <div className="admin-actions"><label className="check-field"><input aria-label={`Включено: ${reminder.definition.title}`} type="checkbox" disabled={busy || loading} checked={reminder.definition.enabled} onChange={e => void change(() => api.request('reminders.enable', { id: reminder.id, expectedVersion: reminder.version, enabled: e.target.checked }), 'Состояние сохранено.')} />Включено</label>
        <button disabled={busy || loading} aria-label={`Изменить напоминание: ${reminder.definition.title}`} onClick={() => { setEditor({ original: reminder, input: { ...reminder.definition } }); setError(''); }}>Изменить</button>
        <button disabled={busy || loading} aria-label={`Удалить напоминание: ${reminder.definition.title}`} onClick={() => setDeleting(reminder.id)}>Удалить</button></div>
      {deleting === reminder.id && <div className="delete-copy" role="group" aria-label="Подтверждение удаления напоминания"><p>Удалить «{reminder.definition.title}»? История подтверждений сохранится.</p><button disabled={busy} onClick={() => setDeleting(null)}>Отмена</button><button className="danger" disabled={busy} onClick={() => void change(() => api.request('reminders.delete', { id: reminder.id, expectedVersion: reminder.version }), 'Расписание удалено; история сохранена.')}>Подтвердить удаление</button></div>}
    </article>)}
    {snapshot && <section className="occurrences" aria-label="Подтверждения напоминаний"><h3>Сегодня и ожидающие подтверждения</h3><p className="admin-help">Каждый пункт подтверждается отдельно. Закрытие окна и «Скрыть» не означают выполнение. Пояс базового расписания: {snapshot.baseTimeZone}.</p>
      {snapshot.truncated && <p role="status">Показаны последние 500 записей; полная история сохранена.</p>}
      {snapshot.occurrences.length === 0 && <p>Ожидающих пунктов пока нет.</p>}
      {snapshot.occurrences.map(item => <article className="admin-row" key={item.id}><div><strong>{item.title}{item.isManualTest ? ' · ТЕСТ' : ''}</strong><p>{item.localDate} · {item.acknowledgment === 'Completed' ? 'Выполнено' : item.acknowledgment === 'Dismissed' ? 'Скрыто — не выполнено' : 'Ожидает'}{item.snoozedUntilUtc ? ` · Отложено до ${new Date(item.snoozedUntilUtc).toLocaleString('ru-RU')}` : ''}</p></div>
        {item.acknowledgment !== 'Completed' && <div className="admin-actions"><button disabled={busy} aria-label={`Выполнено: ${item.title}`} onClick={() => void change(() => api.request('reminders.acknowledge', { id: item.id, completed: true }), 'Выполнение подтверждено вами.')}>Подтвердить выполнение</button>{item.acknowledgment === 'Pending' && <><button disabled={busy} aria-label={`Отложить: ${item.title}`} onClick={() => void change(() => api.request('reminders.snooze', { id: item.id }), 'Отложено на 10 минут.')}>На 10 минут</button><button disabled={busy} aria-label={`Скрыть: ${item.title}`} onClick={() => void change(() => api.request('reminders.acknowledge', { id: item.id, completed: false }), 'Скрыто, но не выполнено.')}>Скрыть</button></>}</div>}
      </article>)}</section>}
  </section>;
}
