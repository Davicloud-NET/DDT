// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { stepDetailText } from "./stepDetail";

describe("stepDetailText", () => {
  it("puts the coded Windows Update details in words", () => {
    expect(stepDetailText("#downloadingUpdates 5")).toBe("Downloading 5 updates");
    expect(stepDetailText("#installingUpdates 1")).toBe("Installing 1 update");
  });

  it("shows anything else as the agent wrote it", () => {
    expect(stepDetailText("7-Zip 24.08")).toBe("7-Zip 24.08");
    expect(stepDetailText("#somethingNew 3")).toBe("#somethingNew 3");
    expect(stepDetailText("#downloadingUpdates")).toBe("#downloadingUpdates");
    expect(stepDetailText("#downloadingUpdates five")).toBe("#downloadingUpdates five");
  });
});
