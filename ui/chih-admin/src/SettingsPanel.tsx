import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import type { PlannerApi } from './bridge';
import { settingsInputSchema, settingsSchema } from './contracts';
import type { Settings, SettingsInput } from './contracts';

export function SettingsPanel({ api, openNative }: { api: PlannerApi; openNative: () => void }) {
  const [snapshot, setSnapshot] = useState<Settings | null>(null), [input, setInput] = useState<SettingsInput | null>(null);
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [reload, setReload] = useState(0);
  const [error, setError] = useState(''), [notice, setNotice] = useState('');
  useEffect(() => {
    let current = true; setLoading(true); setError('');
    api.request('settings.get', {}).then(value => { if (current) { const data = settingsSchema.parse(value); setSnapshot(data); setInput(data.settings); } })
      .catch(cause => { if (current) setError(cause instanceof Error ? cause.message : 'Настройки недоступны.'); }).finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [api, reload]);
  async function save(event: FormEvent) {
    event.preventDefault(); if (!snapshot || !input) return;
    const parsed = settingsInputSchema.safeParse(input); if (!parsed.success) { setError('Проверьте имя, время и интервал комментариев.'); return; }
    setBusy(true); setError(''); setNotice('');
    try { const data = settingsSchema.parse(await api.request('settings.save', { expectedVersion: snapshot.version, settings: parsed.data })); setSnapshot(data); setInput(data.settings); setNotice('Настройки сохранены в профиле Чиха.'); }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Не сохранено. Обновите настройки.'); } finally { setBusy(false); }
  }
  return <section className="task-panel admin-panel" aria-label="Настройки Чиха"><div className="panel-title"><div><h2>Настройки пользователя</h2><span>Общие с нативным окном · один локальный профиль</span></div><button className="subtle" disabled={busy || loading} onClick={() => setReload(n => n + 1)}>Обновить настройки</button></div>
    {loading && <p role="status">Загрузка настроек…</p>}{error && <div className="error" role="alert">{error}</div>}{notice && <div className="notice" role="status">{notice}</div>}
    {snapshot?.error && <div className="error" role="alert">{snapshot.error}</div>}
    {input && snapshot && <form onSubmit={save} className="admin-editor" aria-label="Пользовательские настройки"><fieldset disabled={busy || loading || !snapshot.canWrite}>
      <label className="field">Как вас называть?<input name="ownerDisplayName" maxLength={60} value={input.ownerDisplayName} onChange={e => setInput({ ...input, ownerDisplayName: e.target.value })} /><small>Необязательно. Имя остаётся только в вашем профиле.</small></label>
      <label className="field">Часовой пояс<select name="timeZoneId" value={input.timeZoneId} onChange={e => setInput({ ...input, timeZoneId: e.target.value })}>{snapshot.timeZones.map(zone => <option key={zone.id} value={zone.id}>{zone.label} · {zone.id}</option>)}</select></label>
      <p className="admin-help">Пояс влияет на новые напоминания и «Сегодня» в задачах. Существующие расписания и UTC-отсрочки не переносятся. Язык: русский.</p>
      {(['launchAtSignIn', 'startPetOnly', 'petTopmost', 'reminderNotificationsEnabled', 'personalityEnabled', 'spontaneousCommentsEnabled', 'quietHoursEnabled', 'taskReminderNotificationsEnabled', 'taskSummarySpeechEnabled', 'showTaskTitlesInBubbles', 'taskCompletionReactionsEnabled'] as const).map((key, index) => <label className="check-field" key={key}><input type="checkbox" name={key} checked={input[key]} onChange={e => setInput({ ...input, [key]: e.target.checked })} />{['Запускать при входе в Windows', 'При запуске показывать только кота', 'Кот поверх окон', 'Показывать уведомления напоминаний', 'Характер Чиха', 'Редкие самостоятельные комментарии', 'Тихие часы комментариев', 'Уведомления задач', 'Редкие сводки задач', 'Показывать названия задач в пузырях', 'Реакции на выполнение задач'][index]}</label>)}
      <div className="field-grid"><label className="field">Минимум минут между комментариями<input type="number" min={1} max={1440} name="minimumCommentIntervalMinutes" value={input.minimumCommentIntervalMinutes} onChange={e => setInput({ ...input, minimumCommentIntervalMinutes: Number(e.target.value) })} /></label><label className="field">Тишина с<input type="time" required value={input.quietStart} onChange={e => setInput({ ...input, quietStart: e.target.value })} /></label><label className="field">Тишина до<input type="time" required value={input.quietEnd} onChange={e => setInput({ ...input, quietEnd: e.target.value })} /></label></div>
      <p className="admin-help">Тихие часы расписания напоминаний редактируются в нативном центре. Изменение этих настроек их не заменяет. Размер, скорость и FPS сохраняются без изменений.</p>
      <button type="submit" className="primary">{busy ? 'Сохранение…' : 'Сохранить настройки'}</button>
    </fieldset></form>}
    <div className="admin-actions"><button className="subtle" disabled={busy} onClick={openNative}>Открыть настройки Чиха</button><span className="admin-help">Нативное окно также позволяет повторить знакомство.</span></div>
  </section>;
}
