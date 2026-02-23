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
  await page.getByTestId('repo-create-name').fill(fixture.repositoryName);
  await page.getByTestId('repo-create-git-url').fill(fixture.gitUrl);
  await page.getByTestId('repo-create-default-branch').fill(fixture.defaultBranch);
  await page.getByTestId('repo-create-local-path').fill(fixture.localPath);
  await page.getByTestId('repo-create-submit').click();

  await expect(page.locator('tr').filter({ hasText: fixture.repositoryName }).first()).toBeVisible({ timeout: 90000 });
}

async function openRepositorySettings(page, repositoryName) {
  const row = page.locator('tr').filter({ hasText: repositoryName }).first();
  await row.getByRole('button', { name: 'Open' }).click();
  await expect(page).toHaveURL(/\/settings\/repositories\/.+/);
}

async function setRepositoryDefaultsHarness(page, harness) {
  const harnessName = harness === 'opencode' ? 'OpenCode' : 'Codex';
  await page.getByLabel('Harness').click();
  await page.getByRole('option', { name: harnessName }).click();
  await page.getByRole('button', { name: 'Save Task Defaults' }).click();
  await expect(page.getByText('Task defaults saved.')).toBeVisible({ timeout: 30000 });
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
  expect(payload.toLowerCase()).toContain('task-runtime-pool');
  expect(payload.toLowerCase()).toContain('readiness_blocked');
}

async function waitForRunStateToRender(runStateChip) {
  await expect(runStateChip).toBeVisible({ timeout: 90000 });
  await expect
    .poll(async () => ((await runStateChip.textContent()) || '').trim(), { timeout: 120000 })
    .toMatch(/Queued|Execution in progress|Execution succeeded|Execution failed|Pending approval|Stopped|Archived|Cancelled/i);
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
