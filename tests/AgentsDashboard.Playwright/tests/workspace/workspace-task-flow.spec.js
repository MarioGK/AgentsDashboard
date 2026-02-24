const { test, expect } = require('@playwright/test');
const {
  resolveHarness,
  resolveWorkspaceRepositoryName,
  createAgentsDashboardTaskPrompt,
  expectedFileMarkers,
  ensureRepositoryExists,
  openRepositorySettings,
  setRepositoryDefaultsHarness,
  goToWorkspace,
  ensureRuntimeReady,
  waitForRunStateToRender,
  waitForRunTerminalState,
  assertRunSucceeded,
  assertNoErrorSignals,
  extractChatText,
  assertNoErrorSnackbars
} = require('../helpers/workspace-helpers');

test('workspace task lists repository files without errors', async ({ page, request }, testInfo) => {
  const harness = resolveHarness(testInfo.project.name);
  const repositoryName = resolveWorkspaceRepositoryName();
  const ignoredSnackbarPatterns = [/Git refresh failed:/i];

  await ensureRuntimeReady(request);
  await ensureRepositoryExists(page, repositoryName);
  await openRepositorySettings(page, repositoryName);
  await setRepositoryDefaultsHarness(page, harness);
  await assertNoErrorSnackbars(page, ignoredSnackbarPatterns);

  await goToWorkspace(page, repositoryName);
  await page.getByTestId('workspace-new-task').click();

  const composerInput = page.getByTestId('workspace-composer-input');
  await expect(composerInput).toBeVisible({ timeout: 45000 });
  await expect(composerInput).toBeEnabled({ timeout: 45000 });

  const taskPrompt = createAgentsDashboardTaskPrompt();
  await composerInput.fill(taskPrompt);
  await page.getByTestId('workspace-composer-send').click();
  await assertNoErrorSnackbars(page, ignoredSnackbarPatterns);

  const runStateChip = page.getByTestId('workspace-selected-run-state');
  await waitForRunStateToRender(runStateChip);

  let finalState = await waitForRunTerminalState(runStateChip);
  assertRunSucceeded(finalState);

  await page.getByTestId('workspace-refresh-runs').click();
  finalState = await waitForRunTerminalState(runStateChip);
  assertRunSucceeded(finalState);

  await expect
    .poll(async () => {
      const text = await extractChatText(page);
      return text.length;
    }, { timeout: 120000 })
    .toBeGreaterThanOrEqual(100);

  const chatText = await extractChatText(page);
  assertNoErrorSignals(chatText);
  await assertNoErrorSnackbars(page, ignoredSnackbarPatterns);

  const markers = expectedFileMarkers();
  const matchedMarkers = markers.filter((marker) =>
    chatText.toLowerCase().includes(marker.toLowerCase())
  );

  if (matchedMarkers.length < 3) {
    const missingMarkers = markers.filter((marker) => !matchedMarkers.includes(marker));
    const chatPreview = chatText.slice(0, 1000);

    throw new Error(
      [
        'Expected file list output did not include enough repository markers.',
        `Final state: ${finalState}`,
        `Matched markers (${matchedMarkers.length}): ${matchedMarkers.join(', ') || 'none'}`,
        `Missing markers: ${missingMarkers.join(', ')}`,
        `Chat preview: ${chatPreview}`
      ].join('\n')
    );
  }
});
