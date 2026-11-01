export function IntegrationsPanel() {
  return <section aria-label="Интеграции" className="integrations-grid">
    <article className="integration-card">
      <span className="integration-icon" aria-hidden="true">⌘</span>
      <span className="pill">НЕ ПОДКЛЮЧЕНО</span>
      <h2>GitHub появится позже</h2><strong>Developer Companion</strong>
      <p>Репозитории, задачи GitHub и токены пока не подключены. Личные задачи хранятся отдельно.</p>
      <button type="button" disabled aria-label="GitHub — скоро">Скоро</button>
      <div className="integration-footnote">Coming Soon · Без фоновых подключений.</div>
    </article>
    <article className="integration-card">
      <span className="integration-icon" aria-hidden="true">▦</span>
      <span className="pill">Coming Soon</span>
      <h2>Google Calendar</h2><strong>Calendar Companion</strong>
      <p>Просмотр встреч, расписание на сегодня и напоминания перед событиями — скоро.</p>
      <button type="button" disabled aria-label="Google Calendar — скоро">Скоро</button>
      <div className="integration-footnote">Без подключения, синхронизации и токенов.</div>
    </article>
  </section>;
}
