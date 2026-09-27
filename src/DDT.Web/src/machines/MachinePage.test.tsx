// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type {
  DeploymentStepView,
  DeploymentSummary,
  DeploymentView,
} from "@/deployments/deployments";
import type { MachineLogEntry } from "@/log/log";
import type { MachineSummary } from "@/machines/machines";
import type { SequenceStep, StepKind } from "@/sequences/sequences";
import { newStep } from "@/sequences/steps";
import { nth, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import {
  deploymentSummary,
  deploymentView,
  logLine,
  machineSummary,
  operator,
  sequenceResolution,
  stepView,
  viewer,
} from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import type { Answer, Routes } from "@/test/server";

const now = new Date("2026-09-16T10:06:00Z");
const machineId = "0193a4b2-0000-7000-8000-000000000001";
const runId = "0193a4b2-0000-7000-8000-0000000000d2";
const olderRunId = "0193a4b2-0000-7000-8000-0000000000d1";
const newerRunId = "0193a4b2-0000-7000-8000-0000000000d3";

function run(overrides: Partial<DeploymentSummary> = {}): DeploymentSummary {
  return deploymentSummary({
    id: runId,
    state: "Running",
    stepCount: 6,
    stepIndex: 4,
    stepName: "Apply Windows 11",
    percent: 45,
    phase: "WindowsPE",
    activity: "Step",
    createdUtc: "2026-09-16T10:00:00Z",
    startedUtc: "2026-09-16T10:01:00Z",
    updatedUtc: "2026-09-16T10:05:30Z",
    ...overrides,
  });
}

function machine(overrides: Partial<MachineSummary> = {}): MachineSummary {
  return machineSummary({
    id: machineId,
    state: "Deploying",
    assignedName: "PC-042",
    firstSeenUtc: "2026-09-15T09:00:00Z",
    lastSeenUtc: "2026-09-16T10:05:30Z",
    deployment: run(),
    ...overrides,
  });
}

function planned(
  id: string,
  name: string,
  kind: StepKind,
  more: Partial<Pick<SequenceStep, "conditions" | "continueOnError" | "rebootAfter">> = {},
): SequenceStep {
  return { ...newStep(kind, id), name, ...more };
}

function step(
  index: number,
  id: string,
  name: string,
  kind: string,
  more: Partial<DeploymentStepView> = {},
): DeploymentStepView {
  return stepView({ stepId: id, index, name, kind, ...more });
}

const applyImage = step(4, "s5", "Apply Windows 11", "applyImage", {
  state: "Running",
  percent: 45,
  startedUtc: "2026-09-16T10:05:00Z",
});

function view(overrides: Partial<DeploymentView> = {}): DeploymentView {
  return deploymentView({
    summary: run(),
    machineId,
    definition: {
      version: 1,
      steps: [
        planned("s1", "Partition", "partition"),
        planned("s2", "Check model", "runScript", {
          conditions: [{ variable: "Model", operator: "Equals", value: "Latitude 7440" }],
        }),
        planned("s3", "Optional tool", "runScript", { continueOnError: true }),
        planned("s4", "Restart once", "reboot"),
        planned("s5", "Apply Windows 11", "applyImage"),
        planned("s6", "Set wallpaper", "runScript"),
      ],
    },
    steps: [
      step(0, "s1", "Partition", "partition", {
        state: "Done",
        percent: 100,
        startedUtc: "2026-09-16T10:01:00Z",
        finishedUtc: "2026-09-16T10:01:30Z",
      }),
      step(1, "s2", "Check model", "runScript", {
        state: "Skipped",
        finishedUtc: "2026-09-16T10:01:31Z",
      }),
      step(2, "s3", "Optional tool", "runScript", {
        state: "Failed",
        startedUtc: "2026-09-16T10:01:32Z",
        finishedUtc: "2026-09-16T10:02:00Z",
        error: "The script ended with exit code 3.",
      }),
      step(3, "s4", "Restart once", "reboot", {
        state: "Done",
        percent: 100,
        startedUtc: "2026-09-16T10:02:30Z",
        finishedUtc: "2026-09-16T10:03:00Z",
      }),
      applyImage,
      step(5, "s6", "Set wallpaper", "runScript", { phase: "Windows" }),
    ],
    ...overrides,
  });
}

function logOf(deploymentId: string | null): string {
  return `GET /api/machines/${machineId}/log?limit=500${deploymentId === null ? "" : `&deploymentId=${deploymentId}`}`;
}

// What the page reads: the machine list, the machine's runs, what chooses its sequence, each run and its log.
function answers(
  machines: MachineSummary[],
  history: DeploymentSummary[],
  runs: DeploymentView[],
  lines: MachineLogEntry[] = [],
): Routes {
  return {
    "GET /api/machines": { body: machines },
    [`GET /api/machines/${machineId}/deployments`]: { body: history },
    [`GET /api/machines/${machineId}/sequence`]: {
      body: sequenceResolution({
        source: "Assigned",
        explanation: "operator assigned Install Windows on the web, which comes before every rule.",
      }),
    },
    ...Object.fromEntries(
      runs.flatMap((detail): [string, Answer][] => [
        [`GET /api/deployments/${detail.summary.id}`, { body: detail }],
        [
          logOf(detail.summary.id),
          {
            body: {
              lines: lines.filter((line) => line.deploymentId === detail.summary.id),
              hasOlder: false,
            },
          },
        ],
      ]),
    ),
  };
}

function open(
  routes: Routes,
  options: { path?: string; live?: boolean; user?: typeof operator } = {},
): Promise<RenderedPage> {
  return renderPage({
    path: options.path ?? `/machines/${machineId}`,
    user: options.user ?? operator,
    routes,
    ...(options.live === undefined ? {} : { live: options.live }),
  });
}

function stepsList(): HTMLElement {
  return screen.getByRole("list", { name: "Steps" });
}

// The step with this name in the list of the run's steps.
function stepRow(name: string): HTMLElement {
  const item = within(stepsList()).getByText(name, { selector: "span" }).closest("li");

  if (item === null) {
    throw new Error(`${name} is not a step.`);
  }

  return item;
}

function history(): HTMLElement {
  return screen.getByRole("list", { name: "Runs of this machine" });
}

function panelOf(heading: string): HTMLElement {
  const panel = screen.getByRole("heading", { name: heading }).closest("section");

  if (panel === null) {
    throw new Error(`${heading} is not a panel.`);
  }

  return panel;
}

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

  it("shows a run chosen from the history, and offers to follow a new one", async () => {
    const older = run({
      id: olderRunId,
      state: "Failed",
      stepIndex: 0,
      stepName: "Partition",
      activity: null,
      createdUtc: "2026-09-15T10:00:00Z",
      startedUtc: "2026-09-15T10:01:00Z",
      finishedUtc: "2026-09-15T10:02:00Z",
      updatedUtc: "2026-09-15T10:02:00Z",
      error: "The disk is too small.",
    });
    const olderView = view({
      summary: older,
      steps: [
        step(0, "s1", "Partition", "partition", {
          state: "Failed",
          startedUtc: "2026-09-15T10:01:00Z",
          finishedUtc: "2026-09-15T10:02:00Z",
          error: "The disk is too small.",
        }),
      ],
    });
    const { router } = await open(answers([machine()], [run(), older], [view(), olderView]));

    const runs = await screen.findByRole("list", { name: "Runs of this machine" });
    const items = within(runs).getAllByRole("listitem");
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveAttribute("aria-current", "true");
    expect(items[1]).not.toHaveAttribute("aria-current");
    expect(within(runs).getByText("At step 1 of 6: Partition")).toBeInTheDocument();

    press(within(nth(items, 1)).getByRole("link", { name: "Install Windows" }));

    await waitFor(() => {
      expect(router.state.location.search).toEqual({ run: olderRunId });
    });
    expect(await screen.findByRole("alert")).toHaveTextContent("The disk is too small.");
    expect(within(history()).getAllByRole("listitem")[1]).toHaveAttribute("aria-current", "true");
    expect(screen.getByText("A new run started on this machine.")).toBeInTheDocument();

    press(screen.getByRole("link", { name: "Show it" }));

    await waitFor(() => {
      expect(router.state.location.search).toEqual({});
    });
    expect(await screen.findByText("Step 5 of 6, Windows PE")).toBeInTheDocument();
    expect(screen.queryByText("A new run started on this machine.")).not.toBeInTheDocument();
  });

  it("keeps a run's last state in the history once another run is current", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now });
    const historyCall = `GET /api/machines/${machineId}/deployments`;
    const routes = answers([machine()], [run()], [view()]);
    const { server, hub } = await open(routes);

    await within(await screen.findByRole("list", { name: "Runs of this machine" })).findByText(
      "Running",
    );

    const done = run({
      state: "Done",
      activity: null,
      finishedUtc: "2026-09-16T10:05:50Z",
      updatedUtc: "2026-09-16T10:05:50Z",
    });
    act(() => {
      hub?.push("machineChanged", machine({ state: "Done", deployment: done }));
    });
    await within(history()).findByText("Done");

    const next = run({
      id: newerRunId,
      state: "Assigned",
      stepIndex: null,
      stepName: null,
      percent: 0,
      phase: null,
      activity: null,
      createdUtc: "2026-09-16T10:05:55Z",
      startedUtc: null,
      updatedUtc: "2026-09-16T10:05:55Z",
    });
    routes[historyCall] = { body: [next, done] };
    act(() => {
      hub?.push("machineChanged", machine({ state: "Approved", deployment: next }));
    });

    await waitFor(() => {
      expect(
        within(history())
          .getAllByRole("listitem")
          .map((item) => item.textContent),
      ).toEqual([
        expect.stringMatching(
          /^Install Windows.*Not startedAssigned on the weboperatorNot started$/,
        ),
        expect.stringMatching(/^Install Windows.*DoneAssigned on the weboperator4 min 50 s$/),
      ]);
    });
    expect(server.count(historyCall)).toBe(2);
    expect(server.count("GET /api/machines")).toBe(1);
  });

  it("reads the history and the shown run again after a reconnect", async () => {
    const { server, hub } = await open(answers([machine()], [run()], [view()]));
    const historyCall = `GET /api/machines/${machineId}/deployments`;
    const runCall = `GET /api/deployments/${runId}`;

    await screen.findByRole("list", { name: "Steps" });
    const before = [server.count(historyCall), server.count(runCall)];

    act(() => {
      hub?.loseConnection();
      hub?.reconnect();
    });

    await waitFor(() => {
      expect([server.count(historyCall), server.count(runCall)]).toEqual([
        (before[0] ?? 0) + 1,
        (before[1] ?? 0) + 1,
      ]);
    });
    expect(hub?.invocations.filter((call) => call === `WatchMachine ${machineId}`)).toHaveLength(2);
  });

  it("reads the run every 5 s without a live connection while it is active, and stops once it ended", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true, now });
    const routes = answers([machine()], [run()], [view()]);
    const { server } = await open(routes, { live: false });
    const reads = () => [
      server.count("GET /api/machines"),
      server.count(`GET /api/deployments/${runId}`),
    ];

    await screen.findByText("Step 5 of 6, Windows PE");
    expect(
      screen.getByText("Reading every 5 s, because the live connection is down."),
    ).toBeInTheDocument();
    const [machines = 0, shown = 0] = reads();

    await act(() => vi.advanceTimersByTimeAsync(5_000));
    const [polledMachines = 0, polledShown = 0] = reads();
    expect(polledMachines).toBeGreaterThan(machines);
    expect(polledShown).toBeGreaterThan(shown);

    const done = run({
      state: "Done",
      activity: null,
      finishedUtc: "2026-09-16T10:06:00Z",
      updatedUtc: "2026-09-16T10:06:00Z",
    });
    routes["GET /api/machines"] = { body: [machine({ state: "Done", deployment: done })] };
    routes[`GET /api/deployments/${runId}`] = { body: view({ summary: done }) };
    await act(() => vi.advanceTimersByTimeAsync(5_000));
    await screen.findByText("The run is done");

    // The list goes on being read, as for every list while the connection is down; the ended run is not.
    const [endedMachines = 0, endedShown = 0] = reads();
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    expect(server.count(`GET /api/deployments/${runId}`)).toBe(endedShown);
    expect(server.count("GET /api/machines")).toBeGreaterThan(endedMachines);
  });

  it("lists a new run in the history as soon as the machine list has it", async () => {
    const earlier = run({
      state: "Done",
      activity: null,
      finishedUtc: "2026-09-16T10:05:50Z",
      updatedUtc: "2026-09-16T10:05:50Z",
    });
    const { hub } = await open(
      answers(
        [machine({ state: "Done", deployment: earlier })],
        [earlier],
        [view({ summary: earlier })],
      ),
    );

    await within(await screen.findByRole("list", { name: "Runs of this machine" })).findByText(
      "Done",
    );

    act(() => {
      hub?.push(
        "machineChanged",
        machine({
          state: "Approved",
          deployment: run({
            id: newerRunId,
            title: "Lab setup",
            state: "Assigned",
            stepIndex: null,
            stepName: null,
            percent: 0,
            phase: null,
            activity: null,
            startedUtc: null,
          }),
        }),
      );
    });

    // The server's history does not have it yet.
    expect(await within(history()).findByRole("link", { name: "Lab setup" })).toBeInTheDocument();
    expect(within(history()).getAllByRole("listitem")).toHaveLength(2);
  });

  it("reads again what chooses the sequence once the machine's run changes", async () => {
    const resolutionCall = `GET /api/machines/${machineId}/sequence`;
    const routes = answers([machine({ state: "Pending", deployment: null })], [], []);
    routes[resolutionCall] = {
      body: sequenceResolution({
        source: "ModelRule",
        sequenceId: "0193a4b2-0000-7000-8000-0000000000e1",
        sequenceName: "Install Windows",
        ruleId: "0193a4b2-0000-7000-8000-0000000000f1",
        explanation: "The rule for model Virtual Machine chooses Install Windows.",
      }),
    };
    const { server, hub } = await open(routes);

    expect(
      await screen.findByText("The rule for model Virtual Machine chooses Install Windows."),
    ).toBeInTheDocument();

    routes[resolutionCall] = {
      body: sequenceResolution({
        source: "Assigned",
        explanation: "operator approved PC-042 with Install Windows, which a rule chose.",
      }),
    };
    act(() => {
      hub?.push(
        "machineChanged",
        machine({
          state: "Done",
          deployment: run({
            state: "Done",
            source: "Rule",
            activity: null,
            finishedUtc: "2026-09-16T10:30:00Z",
          }),
        }),
      );
    });

    expect(
      await screen.findByText("operator approved PC-042 with Install Windows, which a rule chose."),
    ).toBeInTheDocument();
    expect(server.count(resolutionCall)).toBe(2);
  });

  it.each([
    {
      phase: "WindowsPE" as const,
      consequence:
        "This stops the run on PC-042. Its disk is left half written; assign a sequence again to deploy it.",
    },
    {
      phase: "Windows" as const,
      consequence:
        "This stops the run on PC-042, which runs in its installed Windows. The agent there stops at its next contact with the server and removes itself; Windows stays installed as it is, with the steps done so far.",
    },
  ])("says what stopping a run in $phase leaves behind", async ({ phase, consequence }) => {
    const current = run({ phase });
    await open(
      answers([machine({ deployment: current })], [current], [view({ summary: current })]),
    );

    press(await screen.findByRole("button", { name: "Stop the run" }));
    const dialog = await screen.findByRole("dialog", { name: "Stop the run on PC-042?" });

    expect(dialog).toHaveAccessibleDescription(consequence);
  });

  it("stops the run and shows how it ended without reading the machines again", async () => {
    const stopped = run({
      state: "Failed",
      activity: null,
      finishedUtc: "2026-09-16T10:06:00Z",
      updatedUtc: "2026-09-16T10:06:00Z",
      error: "Stopped by operator.",
    });
    const routes = answers([machine()], [run()], [view()]);
    routes[`DELETE /api/machines/${machineId}/deployments/current`] = {
      body: machine({ state: "Failed", deployment: stopped }),
    };
    const { server } = await open(routes);

    press(await screen.findByRole("button", { name: "Stop the run" }));
    const dialog = await screen.findByRole("dialog", { name: "Stop the run on PC-042?" });
    press(within(dialog).getByRole("button", { name: "Stop the run" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(await screen.findByRole("alert")).toHaveTextContent("Stopped by operator.");
    expect(screen.queryByRole("button", { name: "Stop the run" })).not.toBeInTheDocument();
    expect(server.count("GET /api/machines")).toBe(1);
  });

  it("says a run from before task sequences has no steps", async () => {
    const legacy = run({
      sequenceId: null,
      title: "Windows 11 Pro",
      state: "Done",
      stepCount: 0,
      stepIndex: null,
      stepName: null,
      activity: null,
      finishedUtc: "2026-09-16T10:30:00Z",
    });

    await open(
      answers(
        [machine({ state: "Done", deployment: legacy })],
        [legacy],
        [view({ summary: legacy, definition: null, steps: [] })],
      ),
    );

    expect(
      await screen.findByText(
        "This deployment ran before task sequences, so it has no steps to show.",
      ),
    ).toBeInTheDocument();
  });

  it("explains that a machine without runs has none yet", async () => {
    await open(answers([machine({ state: "Approved", deployment: null })], [], []));

    expect(await screen.findByText("No runs yet")).toBeInTheDocument();
    expect(
      screen.getByText(
        "Assign a sequence above, or add an assignment rule for its model under Deployment, Assignment rules.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole("heading", { level: 2, name: "Install Windows" })).toBeNull();
  });

  it("shows the machine's Secure Boot state, and that its run may write an image not signed for it", async () => {
    const linux = run({
      title: "Install Linux",
      state: "Done",
      stepCount: 2,
      stepIndex: null,
      stepName: null,
      activity: null,
      finishedUtc: "2026-09-16T10:30:00Z",
    });

    await open(
      answers(
        [machine({ state: "Done", deployment: linux, secureBootEnabled: true })],
        [linux],
        [
          view({
            summary: linux,
            definition: {
              version: 2,
              steps: [
                planned("w1", "Write the disk", "writeRawImage"),
                planned("c1", "Seed", "writeCloudInitSeed"),
              ],
            },
            steps: [
              step(0, "w1", "Write the disk", "writeRawImage", { state: "Done" }),
              step(1, "c1", "Seed", "writeCloudInitSeed", { state: "Done" }),
            ],
            artifacts: [
              {
                stepId: "w1",
                kind: "Image",
                sourceId: "0193a4b2-0000-7000-8000-0000000000a9",
                name: "noble",
                sha256: "00",
                sizeBytes: 1,
              },
            ],
            allowSecureBootMismatch: true,
          }),
        ],
      ),
    );

    expect(
      await screen.findByText(
        "Allowed to write noble although it may not start with Secure Boot on.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Secure Boot", { selector: "dt" }).nextElementSibling,
    ).toHaveTextContent("On");
    expect(
      within(stepRow("Write the disk")).getByText("Write raw disk image, Windows PE"),
    ).toBeInTheDocument();
    expect(
      within(stepRow("Seed")).getByText("Write the cloud-init seed, Windows PE"),
    ).toBeInTheDocument();
  });

  it("says when the machine was removed, also while its page is open", async () => {
    const { hub } = await open(answers([machine()], [run()], [view()]));

    await screen.findByRole("heading", { level: 1, name: "PC-042" });

    act(() => {
      hub?.push("machinesRemoved", { machineIds: [machineId] });
    });

    expect(await screen.findByText("This machine was removed")).toBeInTheDocument();
    expect(
      screen.getByText("It registers as a new machine at its next netboot."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Steps" })).not.toBeInTheDocument();
  });

  it("offers no actions to viewers", async () => {
    await open(answers([machine({ state: "Pending", deployment: null })], [], []), {
      user: viewer,
    });

    await screen.findByRole("heading", { level: 1, name: "PC-042" });
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^More for/ })).not.toBeInTheDocument();
  });

  describe("accessibility", () => {
    it("has no violations with a run under way", async () => {
      await open(
        answers(
          [machine()],
          [run()],
          [view()],
          [logLine(1, { deploymentId: runId, stepId: "s1", level: "Warning" })],
        ),
      );

      await screen.findByText("Line 1");
      await expectNoAxeViolations();
    });

    it("has no violations for a machine without runs", async () => {
      await open(answers([machine({ state: "Pending", deployment: null })], [], []));

      await screen.findByText("No runs yet");
      await expectNoAxeViolations();
    });

    it("has no violations with the stop confirmation open", async () => {
      await open(answers([machine()], [run()], [view()]));

      press(await screen.findByRole("button", { name: "Stop the run" }));
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });

    it("has no violations for a removed machine", async () => {
      await open(answers([], [], []));

      await screen.findByText("This machine was removed");
      await expectNoAxeViolations();
    });
  });
});
