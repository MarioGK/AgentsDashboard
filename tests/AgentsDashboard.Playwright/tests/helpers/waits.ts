import { expect, type Locator, type Page } from '@playwright/test';

const terminalRunStates = new Set([
    'Succeeded',
    'Failed',
    'Cancelled',
    'Obsolete',
]);

export async function waitForTerminalRunState(
    page: Page,
    runStateChip: Locator,
    timeoutMs = 10 * 60_000): Promise<string>
{
    const deadline = Date.now() + timeoutMs;
    let lastState = '';

    while (Date.now() < deadline)
    {
        await expect(runStateChip).toBeVisible({ timeout: 30_000 });
        lastState = (await runStateChip.textContent() ?? '').trim();

        if (terminalRunStates.has(lastState))
        {
            return lastState;
        }

        await page.waitForTimeout(2_000);
        const refreshButton = page.getByTestId('workspace-refresh-runs');
        if (await refreshButton.isVisible().catch(() => false))
        {
            await refreshButton.click();
        }
        await page.waitForTimeout(2_000);
    }

    throw new Error(`Run did not reach a terminal state within ${timeoutMs}ms. Last state: ${lastState || '<empty>'}`);
}
