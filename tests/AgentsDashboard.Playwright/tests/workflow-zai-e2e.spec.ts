import fs from 'node:fs';
import { expect, test, type Page } from '@playwright/test';

import { resolveWorkflowEnvironment } from './helpers/env';
import { createWorkflowTestData } from './helpers/test-data';
import { waitForTerminalRunState } from './helpers/waits';

const workflowEnvironment = resolveWorkflowEnvironment();

async function fillMudFieldByTestId(page: Page, testId: string, value: string): Promise<void>
{
    const container = page.getByTestId(testId);
    await expect(container).toBeVisible({ timeout: 30_000 });
    const input = container.locator('input, textarea').first();
    await expect(input).toBeVisible({ timeout: 30_000 });
    await input.fill(value);
}

async function selectMudOptionByTestId(page: Page, testId: string, optionText: string): Promise<void>
{
    const select = page.getByTestId(testId);
    await expect(select).toBeVisible({ timeout: 30_000 });
    await select.click();

    const option = page.locator('.mud-popover-open .mud-list-item').filter({ hasText: optionText }).first();
    await expect(option).toBeVisible({ timeout: 30_000 });
    await option.click();
}

test.describe.configure({ mode: 'serial' });

test('repository to task to run workflow succeeds with zai', async ({ page }) =>
{
    test.setTimeout(12 * 60_000);

    const data = createWorkflowTestData(workflowEnvironment.repoCloneRoot);
    fs.rmSync(data.localClonePath, { recursive: true, force: true });

    await page.goto('/settings/projects');

    await fillMudFieldByTestId(page, 'repo-create-name', data.repositoryName);
    await fillMudFieldByTestId(page, 'repo-create-git-url', workflowEnvironment.repoRemotePath);
    await fillMudFieldByTestId(page, 'repo-create-default-branch', 'main');
    await fillMudFieldByTestId(page, 'repo-create-local-path', data.localClonePath);

    await page.getByTestId('repo-create-submit').click();
    await expect(page.getByText('Repository created and cloned successfully.')).toBeVisible({ timeout: 180_000 });

    const repositoryRow = page.locator('tr').filter({ hasText: data.repositoryName }).first();
    await expect(repositoryRow).toBeVisible({ timeout: 60_000 });
    await repositoryRow.getByRole('button', { name: 'Open' }).click();

    await expect(page).toHaveURL(/\/settings\/repositories\//, { timeout: 30_000 });
    await page.getByRole('tab', { name: 'Secrets & Webhooks' }).click();

    await fillMudFieldByTestId(page, 'repository-secrets-zai-api-key', workflowEnvironment.zaiApiKey);
    await page.getByTestId('repository-secrets-save').click();
    await expect(page.getByText('Secrets saved.')).toBeVisible({ timeout: 30_000 });

    await page.goto('/workspace');

    const repositoryCard = page.locator('[data-testid^="workspace-repository-card-"]').filter({ hasText: data.repositoryName }).first();
    await expect(repositoryCard).toBeVisible({ timeout: 60_000 });
    await repositoryCard.click();

    await page.getByTestId('workspace-new-task').click();

    await fillMudFieldByTestId(page, 'workspace-create-task-name', data.taskName);
    await selectMudOptionByTestId(page, 'workspace-create-task-harness', 'Zai (GLM-5)');
    await fillMudFieldByTestId(page, 'workspace-create-task-command', 'zai "Return a concise success summary for the workflow e2e run."');
    await page.getByPlaceholder('Describe the goal, constraints, validation, and output format.').fill(data.taskPrompt);

    await page.getByTestId('workspace-create-task-submit').click();
    await expect(page.getByText('Task created.')).toBeVisible({ timeout: 60_000 });

    await page.getByPlaceholder('Add run guidance, press Tab or ArrowRight to accept suggestion').fill(data.runGuidance);
    await page.getByTestId('workspace-composer-submit').click();

    const selectedRunStateChip = page.getByTestId('workspace-selected-run-state');
    const terminalState = await waitForTerminalRunState(page, selectedRunStateChip, 10 * 60_000);

    expect(['Succeeded', 'Obsolete']).toContain(terminalState);

    await page.getByRole('tab', { name: 'Timeline' }).click();
    await expect(page.getByText('No executions found for this task.')).toHaveCount(0);
    await expect(page.getByTestId('workspace-history-panel')).toBeVisible();
});
