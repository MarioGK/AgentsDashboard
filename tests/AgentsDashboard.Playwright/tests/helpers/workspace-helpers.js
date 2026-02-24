const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { expect } = require('@playwright/test');

const onePixelPngBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAE/wJ/lw7h5QAAAABJRU5ErkJggg==';

function resolveHarness(projectName) {
  const normalized = (projectName || '').toLowerCase();
  return normalized.includes('opencode') ? 'opencode' : 'codex';
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
    localPath: path.join(runRoot, 'workspace-repository'),
    defaultBranch: 'main',
    imagePath
  };
}

async function createRepositoryFromSettings(page, fixture) {
  await page.goto('/settings/repositories');
  const nameInput = page.getByRole('textbox', { name: /^Repository Name/ });
  const gitUrlInput = page.getByRole('textbox', { name: /^Git URL/ });
  const defaultBranchInput = page.getByRole('textbox', { name: /^Default Branch/ });
  const localPathInput = page.getByRole('textbox', { name: /^Local Folder/ });

  await nameInput.fill(fixture.repositoryName);
  await expect(nameInput).toHaveValue(fixture.repositoryName);
  await gitUrlInput.fill(fixture.gitUrl);
  await expect(gitUrlInput).toHaveValue(fixture.gitUrl);
  await defaultBranchInput.fill(fixture.defaultBranch);
  await expect(defaultBranchInput).toHaveValue(fixture.defaultBranch);
  await localPathInput.fill(fixture.localPath);
  await expect(localPathInput).toHaveValue(fixture.localPath);
  await page.getByTestId('repo-create-submit').click();

  await expect(page.locator('tr').filter({ hasText: fixture.repositoryName }).first()).toBeVisible({ timeout: 90000 });
}

async function openRepositorySettings(page, repositoryName) {
  const row = page.locator('tr').filter({ hasText: repositoryName }).first();
  await row.getByRole('button', { name: 'Open' }).click();
  await expect(page).toHaveURL(/\/settings\/repositories\/.+/);
}

async function setRepositoryDefaultsHarness(page, harness) {
  if (harness === 'codex') {
    return;
  }

  await expect(page.getByRole('button', { name: 'Save Task Defaults' })).toBeVisible({ timeout: 30000 });
}

async function goToWorkspace(page, repositoryName) {
  await page.goto('/workspace');
  const repositoryCard = page.locator('.workspace-repository-card').filter({ hasText: repositoryName }).first();
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
    .toMatch(/Queued|Running|Execution in progress|Execution succeeded|Execution failed|Pending approval|Stopped|Archived|Cancelled/i);
}

module.exports = {
  resolveHarness,
  createWorkspaceFixture,
  createRepositoryFromSettings,
  openRepositorySettings,
  setRepositoryDefaultsHarness,
  goToWorkspace,
  ensureRuntimeReady,
  waitForRunStateToRender
};
