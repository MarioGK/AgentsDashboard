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

test('workspace composer upload input stays hidden and upload trigger opens chooser', async ({ page }) =>
{
    await page.goto('/workspace', { waitUntil: 'domcontentloaded' });

    const composerInput = page.getByTestId('workspace-composer-file-input');

    if (await composerInput.count() == 0)
    {
        const taskCards = page.locator('[data-testid^="workspace-task-card-"]');
        const taskCount = await taskCards.count();
        if (taskCount > 0)
        {
            await taskCards.first().click();
        }
        else
        {
            const newTaskButton = page.getByTestId('workspace-new-task');
            if (await newTaskButton.isVisible() && await newTaskButton.isEnabled())
            {
                await newTaskButton.click();
            }
        }
    }

    if (await composerInput.count() == 0)
    {
        return;
    }

    await expect(composerInput).toBeHidden();

    const hiddenStyles = await composerInput.evaluate((element) =>
    {
        const computed = window.getComputedStyle(element);
        return {
            position: computed.position,
            width: computed.width,
            height: computed.height,
            opacity: computed.opacity,
            pointerEvents: computed.pointerEvents,
            clipPath: computed.clipPath
        };
    });

    expect(hiddenStyles.position).toBe('absolute');
    expect(hiddenStyles.width).toBe('1px');
    expect(hiddenStyles.height).toBe('1px');
    expect(hiddenStyles.opacity).toBe('0');
    expect(hiddenStyles.pointerEvents).toBe('none');
    expect(hiddenStyles.clipPath).not.toBe('none');

    const uploadTrigger = page.getByTestId('workspace-composer-upload-trigger');
    await expect(uploadTrigger).toBeVisible();

    const fileChooserPromise = page.waitForEvent('filechooser');
    await uploadTrigger.click();
    const fileChooser = await fileChooserPromise;
    const chooserInputTestId = await fileChooser.element().getAttribute('data-testid');

    expect(chooserInputTestId).toBe('workspace-composer-file-input');
});
