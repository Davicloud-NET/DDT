// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { defineConfig } from "@playwright/test";

// Screenshot checks of a few pages in a real browser, against the Vite dev server. The tests answer the API and the
// hub themselves, so they need no DDT server. They run in the Microsoft Edge that comes with Windows, so nothing is
// downloaded. They compare with screens/__screenshots__, taken on Windows because fonts render differently elsewhere.
// `npm run screens` compares, and `npm run screens:update` retakes them after an intended change.
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
