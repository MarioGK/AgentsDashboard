import { expect, test } from '@playwright/test';

const routeAssertions = [
  { path: '/', heading: 'Workspace' },
  { path: '/overview', heading: 'Command Center' },
  { path: '/search', heading: 'Global Search' },
  { path: '/workspace', heading: 'Workspace' },
  { path: '/settings', heading: 'Settings' },
  { path: '/settings/task-runtimes', heading: 'Task Runtime Settings' },
  { path: '/settings/repositories', heading: 'Repositories' },
  { path: '/settings/runs', heading: 'Runs' },
  { path: '/settings/findings', heading: 'Findings' },
  { path: '/providers', heading: 'Provider Settings' },
  { path: '/settings/image-builder', heading: 'Container Image Builder' },
  { path: '/settings/mcp', heading: 'MCP Settings' },
  { path: '/settings/skills', heading: 'Global Prompt Skills' },
  { path: '/settings/sounds', heading: 'Sound Settings' },
  { path: '/settings/alerts', heading: 'Alert Settings' },
  { path: '/settings/system', heading: 'System Settings' },
];

for (const route of routeAssertions)
{
  test(`route ${route.path} loads`, async ({ page }) =>
  {
    await page.goto(route.path, { waitUntil: 'domcontentloaded' });

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
  { id: 'settings-home-skills', heading: 'Global Prompt Skills' },
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
  { id: 'settings-nav-repositories-link', path: '/settings/repositories', heading: 'Repositories' },
  { id: 'settings-nav-runs-link', path: '/settings/runs', heading: 'Runs' },
  { id: 'settings-nav-findings-link', path: '/settings/findings', heading: 'Findings' },
  { id: 'settings-nav-skills-link', path: '/settings/skills', heading: 'Global Prompt Skills' },
  { id: 'settings-nav-sounds-link', path: '/settings/sounds', heading: 'Sound Settings' },
  { id: 'settings-nav-alerts-link', path: '/settings/alerts', heading: 'Alert Settings' },
  { id: 'settings-nav-image-builder-link', path: '/settings/image-builder', heading: 'Container Image Builder' },
  { id: 'settings-nav-mcp-link', path: '/settings/mcp', heading: 'MCP Settings' },
  { id: 'settings-nav-task-runtimes-link', path: '/settings/task-runtimes', heading: 'Task Runtime Settings' },
  { id: 'settings-nav-system-link', path: '/settings/system', heading: 'System Settings' },
];

for (const setting of settingsSideNavTests)
{
  test(`settings side nav ${setting.id} loads`, async ({ page }) =>
  {
    await page.goto('/settings');
    const link = page.getByTestId(setting.id).locator('xpath=ancestor::a').first();
    await expect(link).toHaveAttribute('href', setting.path);
    await Promise.all(
      [
        page.waitForURL(setting.path),
        link.click()
      ]
    );
    await expect(page.getByRole('heading', { name: setting.heading })).toBeVisible();
  });
}
