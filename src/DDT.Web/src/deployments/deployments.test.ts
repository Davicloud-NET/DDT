// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { deploymentSummary } from "@/test/builders";

import { activityLabel, isSilentActivity, isWaiting } from "./deployments";

describe("activityLabel", () => {
  it("names the system a run hands over to, and the one its agent leaves, by the run's phase", () => {
    expect(activityLabel("HandingOver", "WindowsPE")).toBe("Handing over to Windows");
    expect(activityLabel("HandingOver")).toBe("Handing over to Windows");
    expect(activityLabel("HandingOver", "LinuxBoot")).toBe("Handing over to the installed Linux");
    expect(activityLabel("HandingOver", "Linux")).toBe("Handing over to the installed Linux");
    expect(activityLabel("Removing", "Windows")).toBe("Removing the agent from Windows");
    expect(activityLabel("Removing", "Linux")).toBe("Removing the agent from Linux");
    expect(activityLabel("Step", "Linux")).toBeNull();
  });

  // The agent in the installed Linux reports while it waits, so the machine is not silent then.
  it("says that a run in the installed Linux waits for cloud-init", () => {
    expect(activityLabel("WaitingForCloudInit", "Linux")).toBe("Waiting for cloud-init");
    expect(isSilentActivity("WaitingForCloudInit")).toBe(false);
    expect(isSilentActivity("WaitingForWindowsSetup")).toBe(true);
  });
});

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
