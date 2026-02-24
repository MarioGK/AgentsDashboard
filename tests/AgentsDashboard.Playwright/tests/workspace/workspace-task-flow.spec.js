const { test, expect } = require('@playwright/test');
const {
  resolveHarness,
  createWorkspaceFixture,
  createRepositoryFromSettings,
  openRepositorySettings,
  setRepositoryDefaultsHarness,
  goToWorkspace,
  ensureRuntimeReady,
  waitForRunStateToRender
} = require('../helpers/workspace-helpers');

test('workspace task creation, clear, follow-up, and runtime flow are working', async ({ page, request }, testInfo) => {
  const harness = resolveHarness(testInfo.project.name);
  const fixture = await createWorkspaceFixture(testInfo);

  await ensureRuntimeReady(request);
  await createRepositoryFromSettings(page, fixture);
  await openRepositorySettings(page, fixture.repositoryName);
  await setRepositoryDefaultsHarness(page, harness);

  await goToWorkspace(page, fixture.repositoryName);
  await page.getByTestId('workspace-new-task').click();

  const titleLocator = page.getByTestId('workspace-task-title');
  const initialTitle = ((await titleLocator.textContent()) || '').trim();
  const composerInput = page.getByTestId('workspace-composer-input');

  await composerInput.fill(`Validate workspace clear behavior for ${harness}.`);
  await page.getByTestId('workspace-composer-file-input').setInputFiles(fixture.imagePath);
  await expect(page.locator('.workspace-composer-file-chip')).toHaveCount(1);

  await page.getByTestId('workspace-composer-clear').click();
  await expect(composerInput).toHaveValue('');
  await expect(page.locator('.workspace-composer-file-chip')).toHaveCount(0);

  const taskPrompt = `Create and run a workspace task using ${harness}.`;
  await composerInput.fill(taskPrompt);
  await page.getByTestId('workspace-composer-send').click();

  await expect
    .poll(async () => {
      const pendingCount = await page.locator('.workspace-task-card', { hasText: 'Pending' }).count();
      const title = ((await titleLocator.textContent()) || '').trim();
      return pendingCount > 0 || title !== initialTitle;
    }, { timeout: 5000 })
    .toBeTruthy();

  await expect
    .poll(async () => ((await titleLocator.textContent()) || '').trim(), { timeout: 45000 })
    .not.toBe(initialTitle);

  const runStateChip = page.getByTestId('workspace-selected-run-state');
  await waitForRunStateToRender(runStateChip);

  const followUpPrompt = `Follow up check for ${harness}`;
  if (await composerInput.isEnabled()) {
    await composerInput.fill(followUpPrompt);
    await page.getByTestId('workspace-composer-send').click();
    await expect(page.getByTestId('workspace-chat-stream')).toContainText(followUpPrompt, { timeout: 30000 });
  }

  await page.getByTestId('workspace-refresh-runs').click();
  await waitForRunStateToRender(runStateChip);
});
