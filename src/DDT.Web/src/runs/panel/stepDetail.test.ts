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

  it("names the Windows package DISM adds and the cleanup", () => {
    expect(stepDetailText("#addingPackage de-DE language pack (26100)")).toBe(
      "Adding de-DE language pack (26100)",
    );
    expect(stepDetailText("#cleaningUp")).toBe("Cleaning up the image");
    expect(stepDetailText("#addingPackage")).toBe("#addingPackage");
  });

  it("says how far BitLocker has encrypted the disk", () => {
    expect(stepDetailText("#encrypting 74")).toBe("Encrypting the disk (74%)");
    expect(stepDetailText("#encrypting")).toBe("#encrypting");
  });

  it("shows anything else as the agent wrote it", () => {
    expect(stepDetailText("7-Zip 24.08")).toBe("7-Zip 24.08");
    expect(stepDetailText("#somethingNew 3")).toBe("#somethingNew 3");
    expect(stepDetailText("#downloadingUpdates")).toBe("#downloadingUpdates");
    expect(stepDetailText("#downloadingUpdates five")).toBe("#downloadingUpdates five");
  });
});
