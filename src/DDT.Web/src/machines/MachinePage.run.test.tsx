// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { press } from "@/test/aria";
import { logLine } from "@/test/builders";

import {
  answers,
  applyImage,
  machine,
  machineId,
  now,
  open,
  panelOf,
  run,
  runId,
  stepRow,
  view,
} from "./MachinePage.fixtures";

describe("MachinePage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("shows the run's steps with their states, times, errors and why a step was skipped", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now });

    await open(answers([machine()], [run()], [view()]));

    expect(await screen.findByRole("heading", { level: 1, name: "PC-042" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Install Windows" })).toBeInTheDocument();

    // The step that runs, large, and the whole run as a rough share.
    await screen.findByRole("list", { name: "Steps" });
    expect(screen.getByText(/^Revision 3, started .*, running for 5 min 0 s$/)).toBeInTheDocument();
    expect(screen.getByText("Step 5 of 6, Windows PE")).toBeInTheDocument();
    expect(screen.getByText("74%")).toBeInTheDocument();
    expect(screen.getByText("Running for 1 min 0 s")).toBeInTheDocument();
    expect(screen.getByText("Step 3, Optional tool, failed")).toBeInTheDocument();
    expect(screen.getByText("Step 5, Apply Windows 11, running, 45 percent")).toBeInTheDocument();

    const partition = within(stepRow("Partition"));
    expect(partition.getByText("Done")).toBeInTheDocument();
    expect(
      partition.getByText(/^Partition the disk, Windows PE, .* took 30 s$/),
    ).toBeInTheDocument();

    expect(
      within(stepRow("Check model")).getByText(
        'Skipped, because not every condition held: Model is "Latitude 7440", and the machine reports "Virtual Machine".',
      ),
    ).toBeInTheDocument();

    const failed = within(stepRow("Optional tool"));
    expect(failed.getByText("The script ended with exit code 3.")).toBeInTheDocument();
    expect(
      failed.getByText(
        'The run went on, because "Go on when this step fails" is on for this step.',
      ),
    ).toBeInTheDocument();

    const running = within(stepRow("Apply Windows 11"));
    expect(running.getByText("Running 45%")).toBeInTheDocument();
    expect(
      running.getByText(/^Apply image, Windows PE, started .*, running for 1 min 0 s$/),
    ).toBeInTheDocument();

    const pending = within(stepRow("Set wallpaper"));
    expect(pending.getByText("Not started")).toBeInTheDocument();
    expect(pending.getByText("Run script, Windows")).toBeInTheDocument();
    expect(pending.queryByRole("button", { name: "Log" })).not.toBeInTheDocument();

    const timeline = within(panelOf("Timeline"));
    expect(
      timeline.getByText("The machine registered for the first time, from 172.25.132.98"),
    ).toBeInTheDocument();
    expect(timeline.getByText("operator assigned Install Windows on the web")).toBeInTheDocument();
    expect(timeline.getByText("The agent started the run")).toBeInTheDocument();
    expect(
      timeline.getByText("Restarted after step 4, Restart once; back after 2 min 0 s"),
    ).toBeInTheDocument();
  });

  it("shows only a step's log lines once asked from its row", async () => {
    await open(
      answers(
        [machine()],
        [run()],
        [view()],
        [
          logLine(1, { deploymentId: runId, stepId: "s1", message: "Partitioning disk 0" }),
          logLine(2, { deploymentId: runId, stepId: "s5", message: "Applying index 1" }),
        ],
      ),
    );

    expect(await screen.findByText("Applying index 1")).toBeInTheDocument();
    press(within(stepRow("Partition")).getByRole("button", { name: "Log" }));

    expect(await screen.findByText(/^Only the lines of step 1, Partition\./)).toBeInTheDocument();
    expect(screen.getByText("Partitioning disk 0")).toBeInTheDocument();
    expect(screen.queryByText("Applying index 1")).not.toBeInTheDocument();

    press(screen.getByRole("button", { name: "Show every step" }));

    expect(await screen.findByText("Applying index 1")).toBeInTheDocument();
  });

  it("does not say a run went on after the step it was stopped in", async () => {
    const stopped = run({
      state: "Failed",
      stepIndex: 2,
      stepName: "Optional tool",
      percent: 0,
      activity: null,
      finishedUtc: "2026-09-16T10:01:50Z",
      updatedUtc: "2026-09-16T10:01:50Z",
      error: "Stopped by operator.",
    });
    const base = view();
    const steps = base.steps.map((each) =>
      each.index < 2
        ? each
        : each.index === 2
          ? { ...each, finishedUtc: "2026-09-16T10:01:50Z", error: "Stopped by operator." }
          : { ...each, state: "Pending" as const, percent: 0, startedUtc: null, finishedUtc: null },
    );

    await open(
      answers(
        [machine({ state: "Failed", deployment: stopped })],
        [stopped],
        [{ ...base, summary: stopped, steps }],
      ),
    );

    await screen.findByRole("list", { name: "Steps" });
    const failed = within(stepRow("Optional tool"));
    expect(failed.getByText("Stopped by operator.")).toBeInTheDocument();
    expect(failed.queryByText(/The run went on/)).not.toBeInTheDocument();
  });

  it("moves a step on when the server pushes its change, and never back", async () => {
    const { hub } = await open(answers([machine()], [run()], [view()]));

    await screen.findByRole("list", { name: "Steps" });
    expect(hub?.invocations).toContain(`WatchMachine ${machineId}`);
    expect(stepRow("Apply Windows 11").className).not.toMatch(/live-/);

    act(() => {
      hub?.push("runStepChanged", {
        machineId,
        deploymentId: runId,
        step: { ...applyImage, state: "Done", percent: 100, finishedUtc: "2026-09-16T10:07:00Z" },
      });
    });

    await waitFor(() => {
      expect(within(stepRow("Apply Windows 11")).getByText("Done")).toBeInTheDocument();
    });
    // The step that finished flashes in the colour of done; the others stay as they were.
    expect(stepRow("Apply Windows 11")).toHaveClass("live-flash", "live-tone-ok");
    expect(stepRow("Partition").className).not.toMatch(/live-/);

    // An older push that arrives late does not move the step back.
    act(() => {
      hub?.push("runStepChanged", {
        machineId,
        deploymentId: runId,
        step: { ...applyImage, percent: 60 },
      });
    });
    await new Promise((resolve) => {
      setTimeout(resolve, 10);
    });

    expect(within(stepRow("Apply Windows 11")).getByText("Done")).toBeInTheDocument();
  });
});
