// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import axe from "axe-core";
import { expect } from "vitest";

// Runs axe over the whole document, dialogs and popovers included, and fails with each rule it breaks and the
// elements that break it.
export async function expectNoAxeViolations(): Promise<void> {
  const results = await axe.run(document.body, {
    rules: {
      // jsdom neither lays out nor paints, so axe cannot tell the colours text is drawn in. The design system's
      // generator, src/DDT.Design/generate.mjs, checks the contrast of every pair of tokens instead.
      "color-contrast": { enabled: false },
    },
  });

  expect(
    results.violations.map(
      (violation) =>
        `${violation.id}: ${violation.help}\n${violation.nodes.map((node) => `  ${node.html}`).join("\n")}`,
    ),
  ).toEqual([]);
}
