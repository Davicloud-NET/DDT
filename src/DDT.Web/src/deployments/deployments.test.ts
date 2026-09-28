// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { deploymentSummary } from "@/test/builders";

import { isWaiting } from "./deployments";

describe("isWaiting", () => {
  it("says a running run needs someone while it waits for answers or at a pause", () => {
    expect(isWaiting(deploymentSummary({ state: "Running", activity: "Paused" }))).toBe(true);
    expect(isWaiting(deploymentSummary({ state: "Running", activity: "WaitingForInput" }))).toBe(
      true,
    );
    expect(isWaiting(deploymentSummary({ state: "Running", waiting: true }))).toBe(true);
    expect(isWaiting(deploymentSummary({ state: "Running", activity: "Step" }))).toBe(false);
    expect(isWaiting(deploymentSummary({ state: "Failed", activity: "Paused" }))).toBe(false);
    expect(isWaiting(null)).toBe(false);
  });
});
