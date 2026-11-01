import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { IntegrationsPanel } from '../IntegrationsPanel';

describe('local integration placeholders', () => {
  it('retains GitHub and shows the approved Calendar Coming Soon content', () => {
    render(<IntegrationsPanel />);
    expect(screen.getByRole('heading', { name: 'GitHub появится позже' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Google Calendar' })).toBeVisible();
    expect(screen.getByText('Calendar Companion')).toBeVisible();
    expect(screen.getByText('Просмотр встреч, расписание на сегодня и напоминания перед событиями — скоро.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Google Calendar — скоро' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'GitHub — скоро' })).toBeDisabled();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Connect|Подключить/i })).not.toBeInTheDocument();
  });
});
