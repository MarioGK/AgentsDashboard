import { expect, test } from '@playwright/test';

test('home dashboard loads', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Command Center' })).toBeVisible();
});

test('global search page loads', async ({ page }) => {
  await page.goto('/search');

  await expect(page.getByRole('heading', { name: 'Global Search' })).toBeVisible();
});

test('orchestrator settings page loads', async ({ page }) => {
  await page.goto('/settings/orchestrator');

  await expect(page.getByRole('heading', { name: 'Orchestrator Settings' })).toBeVisible();
});

test('workspace task list surface is visible', async ({ page }) => {
  await page.goto('/workspace');

  await expect(page.getByText('Recent Tasks', { exact: true })).toBeVisible();
});
