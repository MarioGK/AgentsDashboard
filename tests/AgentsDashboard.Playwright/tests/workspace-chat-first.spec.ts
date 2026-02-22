import { expect, test } from '@playwright/test';

test('workspace chat-first shell loads and rail toggle is available', async ({ page }) =>
{
    await page.goto('/workspace', { waitUntil: 'domcontentloaded' });

    await expect(page.getByRole('heading', { name: 'Workspace' })).toBeVisible();
    await expect(page.getByTestId('workspace-thread-rail-toggle')).toBeVisible();
    await expect(page.getByTestId('workspace-new-task')).toBeVisible();

    await page.getByTestId('workspace-thread-rail-toggle').click();
    await expect(page.getByTestId('workspace-thread-rail-toggle')).toBeVisible();
});

test('workspace advanced drawer toggles when a thread is active', async ({ page }) =>
{
    await page.goto('/workspace', { waitUntil: 'domcontentloaded' });

    const taskCards = page.locator('[data-testid^="workspace-task-card-"]');
    const taskCount = await taskCards.count();
    if (taskCount == 0)
    {
        return;
    }

    await taskCards.first().click();
    await expect(page.getByTestId('workspace-chat-stream')).toBeVisible();

    const advancedToggle = page.getByTestId('workspace-advanced-toggle');
    await expect(advancedToggle).toBeVisible();

    const drawer = page.getByTestId('workspace-history-panel');

    await advancedToggle.click();
    await expect(drawer).toHaveClass(/workspace-advanced-drawer-open/);
    await expect(page.getByTestId('workspace-advanced-drawer')).toBeVisible();

    await advancedToggle.click();
    await expect(drawer).not.toHaveClass(/workspace-advanced-drawer-open/);
});
