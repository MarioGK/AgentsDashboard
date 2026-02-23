const { defineConfig } = require('@playwright/test');

const baseURL = process.env.BASE_URL || 'https://127.0.0.1:5266';

module.exports = defineConfig({
  testDir: './tests',
  timeout: 180000,
  expect: {
    timeout: 20000
  },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL,
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  projects: [
    {
      name: 'codex',
      use: {
        browserName: 'chromium'
      }
    },
    {
      name: 'opencode',
      use: {
        browserName: 'chromium'
      }
    }
  ]
});
