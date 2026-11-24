const { defineConfig } = require('@playwright/test');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const dataDirectory = path.join(os.tmpdir(), 'wam-browser-' + crypto.randomUUID());

module.exports = defineConfig({
  testDir: './tests/browser',
  fullyParallel: false,
  workers: 1,
  timeout: 90000,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:17644',
    channel: 'msedge',
    headless: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    viewport: { width: 1440, height: 1000 }
  },
  webServer: {
    command: 'dotnet run --project src/WebAgentManager --no-launch-profile',
    url: 'http://127.0.0.1:17644/api/bootstrap',
    timeout: 120000,
    reuseExistingServer: false,
    env: { WAM_PORT: '17644', WAM_DATA_DIR: dataDirectory, ASPNETCORE_ENVIRONMENT: 'Production' }
  }
});
