import { expect, test } from '@playwright/test';

const routeAssertions = [
  { path: '/', heading: 'Command Center' },
  { path: '/overview', heading: 'Command Center' },
  { path: '/search', heading: 'Global Search' },
  { path: '/workspace', heading: 'Workspace' },
  { path: '/settings', heading: 'Settings' },
  { path: '/settings/task-runtimes', heading: 'Task Runtime Settings' },
  { path: '/settings/repositories', heading: 'Repositories' },
  { path: '/settings/runs', heading: 'Runs' },
  { path: '/settings/findings', heading: 'Findings' },
  { path: '/settings/schedules', heading: 'Schedules' },
  { path: '/settings/automations', heading: 'Automations' },
  { path: '/settings/workflows/stages', heading: 'Workflows' },
  { path: '/settings/templates', heading: 'Task Templates' },
  { path: '/settings/providers', heading: 'Provider Settings' },
  { path: '/settings/image-builder', heading: 'Container Image Builder' },
  { path: '/settings/mcp', heading: 'MCP Settings' },
  { path: '/settings/skills', heading: 'Global Prompt Skills' },
  { path: '/settings/session-profiles', heading: 'Session Profiles' },
  { path: '/settings/sounds', heading: 'Sound Settings' },
  { path: '/settings/alerts', heading: 'Alert Settings' },
  { path: '/settings/system', heading: 'System Settings' },
];

for (const route of routeAssertions)
{
  test(`route ${route.path} loads`, async ({ page }) =>
  {
    await page.goto(route.path);

    await expect(page.getByRole('heading', { name: route.heading })).toBeVisible();
  });
}

const navTestCases = [
  { id: 'codex-nav-workspace', heading: 'Workspace' },
  { id: 'codex-nav-overview', heading: 'Command Center' },
  { id: 'codex-nav-search', heading: 'Global Search' },
  { id: 'codex-nav-settings', heading: 'Settings' },
];

test('primary app shell navigation resolves to core surfaces', async ({ page }) =>
{
  await page.goto('/workspace');

  for (const nav of navTestCases)
  {
    await page.getByTestId(nav.id).click();
    await expect(page.getByRole('heading', { name: nav.heading })).toBeVisible();
  }
});

const settingsHomeTests = [
  { id: 'settings-home-repositories', heading: 'Repositories' },
  { id: 'settings-home-runs', heading: 'Runs' },
  { id: 'settings-home-findings', heading: 'Findings' },
  { id: 'settings-home-schedules', heading: 'Schedules' },
  { id: 'settings-home-automations', heading: 'Automations' },
  { id: 'settings-home-workflows-stages', heading: 'Workflows' },
  { id: 'settings-home-templates', heading: 'Task Templates' },
  { id: 'settings-home-skills', heading: 'Global Prompt Skills' },
  { id: 'settings-home-session-profiles', heading: 'Session Profiles' },
  { id: 'settings-home-alerts', heading: 'Alert Settings' },
  { id: 'settings-home-image-builder', heading: 'Container Image Builder' },
  { id: 'settings-home-mcp', heading: 'MCP Settings' },
  { id: 'settings-home-task-runtimes', heading: 'Task Runtime Settings' },
  { id: 'settings-home-sounds', heading: 'Sound Settings' },
  { id: 'settings-home-system', heading: 'System Settings' },
];

for (const setting of settingsHomeTests)
{
  test(`settings home shortcut ${setting.id} loads`, async ({ page }) =>
  {
    await page.goto('/settings');
    await page.getByTestId(setting.id).click();
    await expect(page.getByRole('heading', { name: setting.heading })).toBeVisible();
  });
}

const settingsSideNavTests = [
  { id: 'settings-nav-repositories-link', heading: 'Repositories' },
  { id: 'settings-nav-runs-link', heading: 'Runs' },
  { id: 'settings-nav-findings-link', heading: 'Findings' },
  { id: 'settings-nav-schedules-link', heading: 'Schedules' },
  { id: 'settings-nav-automations-link', heading: 'Automations' },
  { id: 'settings-nav-workflows-stages-link', heading: 'Workflows' },
  { id: 'settings-nav-templates-link', heading: 'Task Templates' },
  { id: 'settings-nav-skills-link', heading: 'Global Prompt Skills' },
  { id: 'settings-nav-session-profiles-link', heading: 'Session Profiles' },
  { id: 'settings-nav-sounds-link', heading: 'Sound Settings' },
  { id: 'settings-nav-alerts-link', heading: 'Alert Settings' },
  { id: 'settings-nav-image-builder-link', heading: 'Container Image Builder' },
  { id: 'settings-nav-mcp-link', heading: 'MCP Settings' },
  { id: 'settings-nav-task-runtimes-link', heading: 'Task Runtime Settings' },
  { id: 'settings-nav-system-link', heading: 'System Settings' },
];

for (const setting of settingsSideNavTests)
{
  test(`settings side nav ${setting.id} loads`, async ({ page }) =>
  {
    await page.goto('/settings');
    await page.getByTestId(setting.id).click();
    await expect(page.getByRole('heading', { name: setting.heading })).toBeVisible();
  });
}
