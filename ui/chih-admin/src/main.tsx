import { createRoot } from 'react-dom/client';
import { App } from './App';
import { createAdminRuntime } from './runtime';
import './styles.css';

void createAdminRuntime().then(runtime => {
  window.addEventListener('pagehide', () => runtime.dispose(), { once: true });
  createRoot(document.getElementById('root')!).render(<App api={runtime.api} runtimeMode={runtime.mode} />);
});
