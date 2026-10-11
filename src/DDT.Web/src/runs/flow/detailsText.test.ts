// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { afterEach, describe, expect, it } from "vitest";

import type { DeploymentStepView } from "@/deployments/deployments";
import type { SequencePhase } from "@/sequences/sequences";
import { deploymentSummary, stepView } from "@/test/builders";
import { leaf } from "@/test/trees";

import { runPath, type PathNode } from "../runPath";
import { timeText } from "./detailsText";

const started = "2026-09-16T10:11:00Z";
const finished = "2026-09-16T10:13:00Z";
const run = deploymentSummary({ state: "Running", startedUtc: "2026-09-16T10:00:00Z" });

// The time of day as the sentence shows it, in the test computer's time zone.
function clock(utc: string, locale = "en"): string {
  return new Date(utc).toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" });
}

function nodeIn(phase: SequencePhase, overrides: Partial<DeploymentStepView> = {}): PathNode {
  const step = stepView({
    stepId: "a",
    kind: "runScript",
    phase,
    state: "Done",
    startedUtc: started,
    finishedUtc: finished,
    ...overrides,
  });
  const [node] = runPath({ version: 5, steps: [leaf("a")] }, [step]).nodes;

  if (node === undefined) {
    throw new Error("The path has no node.");
  }

  return node;
}

async function inGerman() {
  const { messages } = await import("../../locales/de/messages.po");

  i18n.loadAndActivate({ locale: "de", messages });
}

describe("when a node of a run's flow ran", () => {
  afterEach(() => {
    i18n.loadAndActivate({ locale: "en", messages: {} });
  });

  // A phase's label is a name: "Installed Linux". In a sentence it needs its article.
  it("names the phase as a sentence does, for each phase", () => {
    const from = clock(started);
    const to = clock(finished);

    expect(timeText(nodeIn("WindowsPE"), run, 0)).toBe(
      `Ran from ${from} to ${to} in Windows PE, took 2 min 0 s.`,
    );
    expect(timeText(nodeIn("Windows"), run, 0)).toBe(
      `Ran from ${from} to ${to} in Windows, took 2 min 0 s.`,
    );
    expect(timeText(nodeIn("LinuxBoot"), run, 0)).toBe(
      `Ran from ${from} to ${to} in the Linux boot environment, took 2 min 0 s.`,
    );
    expect(timeText(nodeIn("Linux"), run, 0)).toBe(
      `Ran from ${from} to ${to} in the installed Linux, took 2 min 0 s.`,
    );
  });

  it("says since when a step runs, when it was skipped and when it has not started", () => {
    const now = Date.parse(started) + 3 * 60_000;
    const running = nodeIn("Linux", { state: "Running", finishedUtc: null });
    const skipped = nodeIn("LinuxBoot", { state: "Skipped", startedUtc: null });

    expect(timeText(running, run, now)).toBe(
      `Running since ${clock(started)} in the installed Linux, for 3 min 0 s.`,
    );
    expect(timeText(skipped, run, now)).toBe(
      `Skipped at ${clock(finished)} in the Linux boot environment.`,
    );
    expect(timeText(nodeIn("Linux", { startedUtc: null, finishedUtc: null }), run, now)).toBe(
      "Not started yet.",
    );
  });

  it("says it in German with the preposition each phase takes", async () => {
    await inGerman();

    const from = clock(started, "de");
    const to = clock(finished, "de");

    expect(timeText(nodeIn("Linux"), run, 0)).toBe(
      `Lief von ${from} bis ${to} im installierten Linux, dauerte 2 Min. 0 s.`,
    );
    expect(timeText(nodeIn("LinuxBoot"), run, 0)).toBe(
      `Lief von ${from} bis ${to} in der Linux-Startumgebung, dauerte 2 Min. 0 s.`,
    );
    expect(timeText(nodeIn("Windows"), run, 0)).toBe(
      `Lief von ${from} bis ${to} in Windows, dauerte 2 Min. 0 s.`,
    );
  });
});
