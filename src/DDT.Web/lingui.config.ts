// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { defineConfig } from "@lingui/cli";
import { formatter } from "@lingui/format-po";

// English is the source language: the text in the code is the English message. `npm run i18n` extracts it into
// the PO catalogs that translators work on. "pseudo" is a stretched, accented English for finding text that is
// not translated and layouts that break with longer languages.
export default defineConfig({
  sourceLocale: "en",
  locales: ["en", "de", "pseudo"],
  pseudoLocale: "pseudo",
  fallbackLocales: { default: "en" },
  catalogs: [
    {
      path: "<rootDir>/src/locales/{locale}/messages",
      include: ["src"],
      exclude: ["**/*.test.ts", "**/*.test.tsx", "src/test/**"],
    },
  ],
  // Without line numbers, a catalog changes only when its messages do.
  format: formatter({ lineNumbers: false }),
});
