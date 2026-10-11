// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { describe, expect, it } from "vitest";

import { deploymentSummary, machineSummary } from "@/test/builders";

import { runDetail } from "./runDetail";

const now = Date.parse("2026-09-16T10:20:00Z");

function minutesAgo(minutes: number): string {
  return new Date(now - minutes * 60_000).toISOString();
}

describe("what a row says of a Linux run after its hand-over", () => {
  const restarting = deploymentSummary({
    state: "Running",
    phase: "Linux",
    activity: "Restarting",
    stepIndex: 1,
    stepName: "Write the cloud-init seed",
  });

  it("says since when the installed Linux is awaited", () => {
    const machine = machineSummary({
      lastSeenUtc: minutesAgo(3),
      handedOverUtc: minutesAgo(2),
      deployment: restarting,
    });

    expect(runDetail(machine, now)).toBe(
      "Handed over 2 minutes ago, no call from the installed Linux yet",
    );
  });

  it("says that there is still no call once a first start should be over", () => {
    const machine = machineSummary({ handedOverUtc: minutesAgo(12), deployment: restarting });

    expect(runDetail(machine, now)).toBe(
      "Handed over 12 minutes ago, still no call from the installed Linux",
    );
  });

  // The boot environment saw the machine again and sent it back to its disk, at the time the server names.
  it("says that the machine started from the network again, and for which time", () => {
    const once = machineSummary({
      handedOverUtc: minutesAgo(1),
      bootReturns: 1,
      deployment: restarting,
    });
    const twice = machineSummary({
      handedOverUtc: minutesAgo(12),
      bootReturns: 2,
      deployment: restarting,
    });

    expect(runDetail(once, now)).toBe(
      "Started from the network again 1 minute ago (1st time), still no call from the installed Linux",
    );
    expect(runDetail(twice, now)).toBe(
      "Started from the network again 12 minutes ago (2nd time), still no call from the installed Linux",
    );
  });

  it("says it in German", async () => {
    const { messages } = await import("../../locales/de/messages.po");
    const twice = machineSummary({
      handedOverUtc: minutesAgo(2),
      bootReturns: 2,
      deployment: restarting,
    });

    i18n.loadAndActivate({ locale: "de", messages });

    try {
      expect(runDetail(twice, now)).toBe(
        "Erneut per Netzwerk gestartet vor 2 Minuten (zum 2. Mal), immer noch keine Meldung vom installierten Linux",
      );
    } finally {
      i18n.loadAndActivate({ locale: "en", messages: {} });
    }
  });

  // A step there asked for the restart, and the machine is known to come back.
  it("says that the machine restarts once the installed Linux has called", () => {
    const machine = machineSummary({
      agentEnvironment: "Linux",
      lastSeenUtc: minutesAgo(1),
      handedOverUtc: null,
      deployment: restarting,
    });

    expect(runDetail(machine, now)).toBe("Restarting, last contact 1 minute ago");
  });

  it("leaves a run that installs Windows its own words", () => {
    const windows = deploymentSummary({
      state: "Running",
      phase: "Windows",
      activity: "Restarting",
    });
    const machine = machineSummary({
      lastSeenUtc: minutesAgo(4),
      handedOverUtc: minutesAgo(4),
      deployment: windows,
    });

    expect(runDetail(machine, now)).toBe("Restarting, last contact 4 minutes ago");
  });
});
