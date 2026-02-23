const { test } = require('@playwright/test');
const { ensureRuntimeReady } = require('../helpers/workspace-helpers');

test('ready endpoint reports task runtime health', async ({ request }) => {
  await ensureRuntimeReady(request);
});
