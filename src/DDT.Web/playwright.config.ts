// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { defineConfig } from "@playwright/test";

// Screenshot checks of a few pages in a real browser, against the Vite dev server with the API and the hub answered by
// the tests themselves, so they need no DDT server. They run in the Microsoft Edge that Windows has, so nothing is
// downloaded, and compare with the screenshots in screens/__screenshots__, which are taken on Windows: fonts render
// differently elsewhere. `npm run screens` compares, `npm run screens:update` takes them again after a wanted change.
const port = "5190";

export default defineConfig({
  testDir: "screens",
  snapshotPathTemplate: "{testDir}/__screenshots__/{arg}{ext}",
  fullyParallel: true,
  reporter: [["list"]],
  expect: {
    toHaveScreenshot: { maxDiffPixelRatio: 0.002, animations: "disabled", caret: "hide" },
  },
  use: {
    baseURL: `http://localhost:${port}`,
    channel: "msedge",
    locale: "en-US",
    timezoneId: "Europe/Vienna",
    viewport: { width: 1440, height: 900 },
  },
  webServer: {
    command: `npx vite --port ${port} --strictPort`,
    url: `http://localhost:${port}`,
    reuseExistingServer: false,
    timeout: 120_000,
  },
});
