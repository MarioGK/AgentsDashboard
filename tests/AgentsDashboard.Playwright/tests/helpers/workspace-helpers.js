const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { expect } = require('@playwright/test');

const onePixelPngBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAE/wJ/lw7h5QAAAABJRU5ErkJggg==';

function resolveHarness(projectName) {
  const forcedHarness = (process.env.PLAYWRIGHT_E2E_HARNESS || '').trim().toLowerCase();
  if (forcedHarness === 'codex' || forcedHarness === 'opencode') {
    return forcedHarness;
  }

  const normalized = (projectName || '').toLowerCase();
  if (normalized.includes('opencode')) {
    const allowOpenCode = (process.env.PLAYWRIGHT_E2E_ENABLE_OPENCODE || '').trim().toLowerCase();
    return allowOpenCode === 'true' ? 'opencode' : 'codex';
  }

  return 'codex';
}

function resolveRemotePath() {
  const remotePath = process.env.PLAYWRIGHT_E2E_REPO_REMOTE_PATH;
  if (!remotePath) {
    throw new Error('PLAYWRIGHT_E2E_REPO_REMOTE_PATH must be set for workspace Playwright tests.');
  }

  return remotePath;
}

function resolveCloneRoot() {
  return process.env.PLAYWRIGHT_E2E_REPO_CLONE_ROOT || path.join(os.tmpdir(), 'agentsdashboard-playwright-clones');
}

function createAgentsDashboardTaskPrompt() {
  return 'List all files in the repostory';
}

function resolveWorkspaceRepositoryName() {
  const configuredName = (process.env.PLAYWRIGHT_E2E_REPOSITORY_NAME || 'AgentsDashboard').trim();
  return configuredName || 'AgentsDashboard';
}

function expectedFileMarkers() {
  return [
    'README.md',
    'AGENTS.md',
    'src/AgentsDashboard.slnx',
    'src/AgentsDashboard.ControlPlane/Program.cs',
    'src/AgentsDashboard.TaskRuntime/Program.cs'
  ];
}

async function createWorkspaceFixture(testInfo) {
  const remotePath = resolveRemotePath();
  const cloneRoot = resolveCloneRoot();
  await fs.mkdir(cloneRoot, { recursive: true });

  const runId = `${Date.now()}-${Math.random().toString(16).slice(2, 8)}-${(testInfo.project.name || 'default').toLowerCase()}`;
  const runRoot = path.join(cloneRoot, runId);
  await fs.mkdir(runRoot, { recursive: true });

  const imagePath = path.join(runRoot, 'sample.png');
  await fs.writeFile(imagePath, Buffer.from(onePixelPngBase64, 'base64'));

  return {
    repositoryName: `pw-${runId}`.replace(/[^a-zA-Z0-9-]/g, '-'),
    gitUrl: remotePath,
    localPath: `workspace-${runId}`.replace(/[^a-zA-Z0-9-]/g, '-'),
    defaultBranch: 'main',
    imagePath
  };
}

async function createRepositoryFromSettings(page, fixture) {
  await page.goto('/settings/repositories');
  const nameInput = page.getByRole('textbox', { name: /^Repository Name/ });
  const gitUrlInput = page.getByRole('textbox', { name: /^Git URL/ });
  const defaultBranchInput = page.getByRole('textbox', { name: /^Default Branch/ });
  const localPathInput = page.getByTestId('repo-create-local-path');

  await nameInput.fill(fixture.repositoryName);
  await expect(nameInput).toHaveValue(fixture.repositoryName);
  await gitUrlInput.fill(fixture.gitUrl);
  await expect(gitUrlInput).toHaveValue(fixture.gitUrl);
  await defaultBranchInput.fill(fixture.defaultBranch);
  await expect(defaultBranchInput).toHaveValue(fixture.defaultBranch);

  if (await localPathInput.count()) {
    await localPathInput.fill(fixture.localPath);
    await expect(localPathInput).toHaveValue(fixture.localPath);
  }

  await page.getByTestId('repo-create-submit').click();

  await expect(page.locator('tr').filter({ hasText: fixture.repositoryName }).first()).toBeVisible({ timeout: 90000 });
}

async function ensureRepositoryExists(page, repositoryName) {
  await page.goto('/settings/repositories');

  const row = page.locator('tr').filter({ hasText: repositoryName }).first();
  if (await row.count()) {
    await expect(row).toBeVisible({ timeout: 30000 });
    return;
  }

  const rows = page.locator('tbody tr');
  const availableRepositories = [];
  const rowCount = await rows.count();
  for (let index = 0; index < rowCount; index += 1) {
    const nameCellText = ((await rows.nth(index).locator('td').first().textContent()) || '').trim();
    if (nameCellText) {
      availableRepositories.push(nameCellText);
    }
  }

  throw new Error(
    `Repository "${repositoryName}" was not found in settings. Available repositories: ${availableRepositories.join(', ') || 'none'}.`
  );
}

async function openRepositorySettings(page, repositoryName) {
  const row = page.locator('tr').filter({ hasText: repositoryName }).first();
  await row.getByRole('button', { name: 'Open' }).click();
  await expect(page).toHaveURL(/\/settings\/repositories\/.+/);
}

async function setRepositoryDefaultsHarness(page, harness) {
  const normalizedHarness = (harness || 'codex').toLowerCase();
  const optionLabel = normalizedHarness === 'opencode' ? 'OpenCode' : 'Codex';

  const harnessInput = page.getByTestId('repository-task-defaults-harness');
  await expect(harnessInput).toHaveCount(1, { timeout: 30000 });

  const currentHarness = ((await harnessInput.inputValue()) || '').trim().toLowerCase();
  if (currentHarness !== normalizedHarness) {
    const harnessTrigger = harnessInput
      .locator('xpath=following-sibling::div[contains(@class, "mud-select-input")]')
      .first();
    await expect(harnessTrigger).toBeVisible({ timeout: 30000 });
    await harnessTrigger.click();

    const option = page.locator('.mud-popover-open .mud-list-item').filter({ hasText: optionLabel }).first();
    await expect(option).toBeVisible({ timeout: 15000 });
    await option.click();
    await expect(harnessInput).toHaveValue(normalizedHarness);
  }

  const saveButton = page.getByRole('button', { name: 'Save Task Defaults' });
  await saveButton.click();
}

async function goToWorkspace(page, repositoryName) {
  await page.goto('/workspace');
  const repositoryCard = page.locator('.workspace-repository-card').filter({ hasText: repositoryName }).first();
  if (!await repositoryCard.count()) {
    const repositoryCards = page.locator('.workspace-repository-card');
    const cardCount = await repositoryCards.count();
    const availableRepositories = [];
    for (let index = 0; index < cardCount; index += 1) {
      const cardText = ((await repositoryCards.nth(index).textContent()) || '').trim();
      if (cardText) {
        availableRepositories.push(cardText);
      }
    }

    throw new Error(
      `Workspace repository card "${repositoryName}" was not found. Available cards: ${availableRepositories.join(', ') || 'none'}.`
    );
  }

  await expect(repositoryCard).toBeVisible({ timeout: 30000 });
  await repositoryCard.click();
}

async function ensureRuntimeReady(request) {
  const response = await request.get('/ready');
  expect(response.ok()).toBeTruthy();
  const payload = await response.text();
  const normalizedPayload = payload.toLowerCase();
  expect(normalizedPayload).toContain('task-runtime-pool');
  if (normalizedPayload.includes('readiness_blocked')) {
    return;
  }

  const parsedPayload = JSON.parse(payload);
  const runtimePoolCheck = parsedPayload?.checks?.['task-runtime-pool'];
  expect(runtimePoolCheck).toBeTruthy();
  const runtimePoolStatus = (runtimePoolCheck.status || '').toLowerCase();
  expect(['healthy', 'degraded', 'unhealthy']).toContain(runtimePoolStatus);
}

async function waitForRunStateToRender(runStateChip) {
  await expect(runStateChip).toBeVisible({ timeout: 90000 });
  await expect
    .poll(async () => ((await runStateChip.textContent()) || '').trim(), { timeout: 120000 })
    .toMatch(/Queued|Running|Execution in progress|Execution succeeded|Execution failed|Pending approval|Stopped|Archived|Cancelled|Obsolete/i);
}

async function waitForRunTerminalState(runStateChip) {
  const deadline = Date.now() + 180000;
  const terminalPattern = /Execution succeeded|Succeeded|Execution failed|Failed|Stopped|Archived|Cancelled|Obsolete/i;

  while (Date.now() < deadline) {
    const currentState = ((await runStateChip.textContent()) || '').trim();
    if (terminalPattern.test(currentState)) {
      return currentState;
    }

    await runStateChip.page().waitForTimeout(1000);
  }

  throw new Error('Timed out waiting for a terminal run state.');
}

function assertRunSucceeded(finalState) {
  const normalized = (finalState || '').toLowerCase();
  if (normalized.includes('succeeded') || normalized.includes('obsolete')) {
    return;
  }

  throw new Error(`Run did not succeed. Final state was: "${finalState}".`);
}

function assertNoErrorSignals(chatText) {
  const errorPatterns = [
    /\berror\s*:/i,
    /\bfatal\b/i,
    /\bexception\b/i,
    /\btraceback\b/i,
    /\bpermission denied\b/i,
    /\brepository not found\b/i,
    /\bexecution failed\b/i
  ];

  for (const pattern of errorPatterns) {
    if (pattern.test(chatText)) {
      throw new Error(`Detected error signal in chat output matching ${pattern}.`);
    }
  }
}

async function extractChatText(page) {
  const chatStream = page.getByTestId('workspace-chat-stream');
  await expect(chatStream).toBeVisible({ timeout: 90000 });
  return ((await chatStream.textContent()) || '').trim();
}

async function assertNoErrorSnackbars(page, ignoredPatterns = []) {
  const errorSnackbars = page.locator('#mud-snackbar-container .mud-snackbar.mud-alert-filled-error');
  const count = await errorSnackbars.count();
  if (count === 0) {
    return;
  }

  const unexpectedMessages = [];
  for (let index = 0; index < count; index += 1) {
    const snackbar = errorSnackbars.nth(index);
    const message = ((await snackbar.textContent()) || '').trim();
    if (!message) {
      continue;
    }

    const shouldIgnore = ignoredPatterns.some((pattern) => {
      if (pattern instanceof RegExp) {
        return pattern.test(message);
      }

      return message.includes(String(pattern));
    });

    if (shouldIgnore) {
      const closeButton = snackbar.getByRole('button', { name: 'Close' });
      if (await closeButton.count()) {
        await closeButton.click();
      }
      continue;
    }

    unexpectedMessages.push(message);
  }

  if (unexpectedMessages.length > 0) {
    throw new Error(`Detected error snackbar(s): ${unexpectedMessages.join(' | ')}`);
  }
}

module.exports = {
  resolveHarness,
  resolveWorkspaceRepositoryName,
  createAgentsDashboardTaskPrompt,
  expectedFileMarkers,
  createWorkspaceFixture,
  createRepositoryFromSettings,
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
};
