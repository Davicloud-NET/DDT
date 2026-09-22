// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import type {
  DeploymentStepView,
  DeploymentSummary,
  DeploymentView,
} from "@/deployments/deployments";
import type { LiveConnection, MachineWatchHandlers } from "@/live/liveConnection";
import { LiveContext } from "@/live/LiveContext";
import type { MachineSummary } from "@/machines/machines";
import { machineSearch } from "@/machines/machineSearch";
import type { SequenceStep } from "@/sequences/sequences";

import { MachineDetailPage } from "./MachineDetailPage";

const now = new Date("2026-09-16T10:06:00Z");
const machineId = "0193a4b2-0000-7000-8000-000000000001";
const runId = "0193a4b2-0000-7000-8000-0000000000d2";
const olderRunId = "0193a4b2-0000-7000-8000-0000000000d1";

function run(overrides: Partial<DeploymentSummary>): DeploymentSummary {
  return {
    id: runId,
    sequenceId: "0193a4b2-0000-7000-8000-0000000000e1",
    title: "Install Windows",
    state: "Running",
    source: "Web",
    requestedBy: "operator",
    stepCount: 6,
    stepIndex: 4,
    stepName: "Apply Windows 11",
    percent: 45,
    phase: "WindowsPE",
    activity: "Step",
    createdUtc: "2026-09-16T10:00:00Z",
    startedUtc: "2026-09-16T10:01:00Z",
    finishedUtc: null,
    updatedUtc: "2026-09-16T10:05:30Z",
    error: null,
    ...overrides,
  };
}

function machine(overrides: Partial<MachineSummary>): MachineSummary {
  return {
    id: machineId,
    state: "Deploying",
    smbiosUuid: "44454c4c-5700-1038-8036-b7c04f5a344a",
    primaryMac: "00155D010203",
    macAddresses: ["00155D010203"],
    manufacturer: "Microsoft Corporation",
    model: "Virtual Machine",
    serialNumber: "1234",
    assignedName: "PC-042",
    agentVersion: "1.0.0",
    firstSeenUtc: "2026-09-15T09:00:00Z",
    lastSeenUtc: "2026-09-16T10:05:30Z",
    lastSeenAddress: "172.25.132.98",
    signedInBy: null,
    firstSeenAddress: "172.25.132.98",
    everApproved: true,
    disks: "Disk 0: Msft Virtual Disk, 64 GB, SCSI",
    eligibleDiskCount: 1,
    deployment: run({}),
    ...overrides,
  };
}

function planned(
  id: string,
  name: string,
  kind: SequenceStep["kind"],
  more: object = {},
): SequenceStep {
  return {
    id,
    name,
    kind,
    conditions: [],
    continueOnError: false,
    rebootAfter: false,
    ...more,
  } as SequenceStep;
}

function step(
  index: number,
  id: string,
  name: string,
  kind: string,
  more: Partial<DeploymentStepView>,
): DeploymentStepView {
  return {
    stepId: id,
    index,
    name,
    kind,
    phase: "WindowsPE",
    state: "Pending",
    percent: 0,
    startedUtc: null,
    finishedUtc: null,
    error: null,
    ...more,
  };
}

const applyImage = step(4, "s5", "Apply Windows 11", "applyImage", {
  state: "Running",
  percent: 45,
  startedUtc: "2026-09-16T10:05:00Z",
});

function view(overrides: Partial<DeploymentView>): DeploymentView {
  return {
    summary: run({}),
    machineId,
    sequenceRevision: 3,
    ruleId: null,
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
    artifacts: [],
    ...overrides,
  };
}

const operator: CurrentUser = {
  id: "u",
  userName: "operator",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  roles: ["Operator"],
};

interface Answer {
  status?: number;
  body?: unknown;
}

// A connection that is up and hands the page's watches to the test, which sends events to all of them.
function fakeLive() {
  const watches = new Set<{ id: string; handlers: MachineWatchHandlers }>();
  const live: LiveConnection = {
    start: () => undefined,
    stop: () => undefined,
    status: () => "live",
    onStatusChange: () => () => undefined,
    watchMachine: (id, handlers) => {
      const watch = { id, handlers };
      watches.add(watch);

      return () => {
        watches.delete(watch);
      };
    },
  };

  return {
    live,
    watcher: (id: string): MachineWatchHandlers => {
      const own = [...watches].filter((watch) => watch.id === id).map((watch) => watch.handlers);

      if (own.length === 0) {
        throw new Error(`Nothing watches ${id}.`);
      }

      return {
        onRunStepChanged: (event) => {
          own.forEach((handlers) => handlers.onRunStepChanged?.(event));
        },
        onLogAppended: (event) => {
          own.forEach((handlers) => handlers.onLogAppended?.(event));
        },
        onReconnect: () => {
          own.forEach((handlers) => handlers.onReconnect?.());
        },
      };
    },
  };
}

// Answers the given "METHOD path" requests; everything else gets a 404.
function renderAt(path: string, answers: Record<string, Answer>) {
  const calls: string[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = (input instanceof Request ? input.url : input.toString()).replace(
        "http://localhost",
        "",
      );
      const call = `${init?.method ?? "GET"} ${url}`;
      calls.push(call);
      const answer = answers[call];

      return Promise.resolve(
        answer === undefined
          ? new Response(null, { status: 404 })
          : new Response(JSON.stringify(answer.body ?? null), { status: answer.status ?? 200 }),
      );
    }),
  );
  vi.stubGlobal("scrollTo", vi.fn());

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const rootRoute = createRootRoute();
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      createRoute({ getParentRoute: () => rootRoute, path: "/", component: () => "Machines" }),
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/machines/$machineId",
        validateSearch: machineSearch,
        component: MachineDetailPage,
      }),
    ]),
    history: createMemoryHistory({ initialEntries: [path] }),
  });
  const { live, watcher } = fakeLive();

  render(
    <QueryClientProvider client={queryClient}>
      <LiveContext value={live}>
        <RouterProvider router={router} />
      </LiveContext>
    </QueryClientProvider>,
  );

  return { calls, router, watcher };
}

function standardAnswers(
  machines: MachineSummary[],
  history: DeploymentSummary[],
  runs: DeploymentView[],
): Record<string, Answer> {
  return {
    "GET /api/auth/me": { body: operator },
    "GET /api/machines": { body: machines },
    [`GET /api/machines/${machineId}/deployments`]: { body: history },
    [`GET /api/machines/${machineId}/sequence`]: {
      body: {
        source: "Assigned",
        sequenceId: null,
        sequenceName: null,
        ruleId: null,
        problemCount: 0,
        explanation: "operator assigned Install Windows on the web, which comes before every rule.",
      },
    },
    ...Object.fromEntries(
      runs.map((detail) => [`GET /api/deployments/${detail.summary.id}`, { body: detail }]),
    ),
  };
}

function nth<T>(items: readonly T[], index: number): T {
  const item = items[index];

  if (item === undefined) {
    throw new Error(`There is no item ${String(index)}.`);
  }

  return item;
}

function stepRow(name: string): HTMLElement {
  const item = screen.getByText(name, { selector: "span" }).closest("li");

  if (item === null) {
    throw new Error(`${name} is not a step.`);
  }

  return item;
}

describe("MachineDetailPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("shows the run's steps with their states, times, errors and why a step was skipped", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now });

    renderAt(`/machines/${machineId}`, standardAnswers([machine({})], [run({})], [view({})]));

    expect(await screen.findByRole("heading", { level: 1, name: "PC-042" })).toBeInTheDocument();
    expect(
      screen.getByText(
        "operator assigned Install Windows on the web, which comes before every rule.",
      ),
    ).toBeInTheDocument();

    const overview = screen.getByRole("region", { name: "Run" });
    expect(within(overview).getByText("Step 5 of 6: Apply Windows 11, 45%")).toBeInTheDocument();
    expect(within(overview).getByText("Running for 5 min 0 s")).toBeInTheDocument();

    await screen.findByRole("region", { name: "Steps" });
    expect(screen.getByRole("heading", { level: 3, name: "Windows PE" })).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 3, name: "Windows, after the hand-over" }),
    ).toBeInTheDocument();

    expect(within(stepRow("Partition")).getByText("Done")).toBeInTheDocument();
    expect(within(stepRow("Partition")).getByText(/took 30 s$/)).toBeInTheDocument();

    expect(
      within(stepRow("Check model")).getByText(
        'Skipped, because not every condition held: Model is "Latitude 7440", and the machine reports "Virtual Machine".',
      ),
    ).toBeInTheDocument();

    const failed = within(stepRow("Optional tool"));
    expect(failed.getByText("The script ended with exit code 3.")).toBeInTheDocument();
    expect(
      failed.getByText("The run continued, because Continue on error is on for this step."),
    ).toBeInTheDocument();

    const running = within(stepRow("Apply Windows 11"));
    expect(running.getByText("Running, 45%")).toBeInTheDocument();
    expect(running.getByText(/running for 1 min 0 s$/)).toBeInTheDocument();
    expect(running.getByRole("progressbar", { name: "Apply Windows 11 progress" })).toHaveAttribute(
      "value",
      "45",
    );

    expect(within(stepRow("Set wallpaper")).getByText("Pending")).toBeInTheDocument();

    const timeline = screen.getByRole("region", { name: "Timeline" });
    expect(
      within(timeline).getByText("Restarted after step 4, Restart once; back after 2 min 0 s"),
    ).toBeInTheDocument();
    expect(
      within(timeline).getByText("operator assigned Install Windows on the web"),
    ).toBeInTheDocument();
  });

  it("moves a step on when the server pushes its change", async () => {
    const { watcher } = renderAt(
      `/machines/${machineId}`,
      standardAnswers([machine({})], [run({})], [view({})]),
    );

    await screen.findByText("Running, 45%");

    act(() => {
      watcher(machineId).onRunStepChanged?.({
        machineId,
        deploymentId: runId,
        step: { ...applyImage, state: "Done", percent: 100, finishedUtc: "2026-09-16T10:07:00Z" },
      });
    });

    // The query cache tells its observers after the current task.
    await waitFor(() => {
      expect(within(stepRow("Apply Windows 11")).getByText("Done")).toBeInTheDocument();
    });
    expect(within(stepRow("Apply Windows 11")).queryByRole("progressbar")).not.toBeInTheDocument();

    // An older push that arrives late does not move the step back.
    act(() => {
      watcher(machineId).onRunStepChanged?.({
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
    const { router } = renderAt(
      `/machines/${machineId}`,
      standardAnswers([machine({})], [run({}), older], [view({}), olderView]),
    );

    const history = await screen.findByRole("region", { name: "History" });
    const rows = await within(history).findAllByRole("row");
    expect(rows).toHaveLength(3);
    expect(rows[1]).toHaveAttribute("aria-current", "true");
    expect(within(history).getByText("At step 1 of 6: Partition")).toBeInTheDocument();

    fireEvent.click(nth(within(history).getAllByRole("link", { name: "Install Windows" }), 1));

    await waitFor(() => {
      expect(router.state.location.search).toEqual({ run: olderRunId });
    });
    expect(
      await within(screen.getByRole("region", { name: "Run" })).findByText(
        "The disk is too small.",
      ),
    ).toBeInTheDocument();
    expect(
      await screen.findByText("A new run started on this machine.", { exact: false }),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole("link", { name: "Show it" }));

    await waitFor(() => {
      expect(router.state.location.search).toEqual({});
    });
    expect(
      await within(screen.getByRole("region", { name: "Run" })).findByText(
        "Step 5 of 6: Apply Windows 11, 45%",
      ),
    ).toBeInTheDocument();
  });

  it.each([
    {
      phase: "WindowsPE" as const,
      consequence:
        "This stops the deployment on PC-042. Its disk is left half written; assign a sequence again to deploy it.",
    },
    {
      phase: "Windows" as const,
      consequence:
        "This stops the deployment on PC-042, which runs in its installed Windows. The agent there stops at its next contact with the server and removes itself; Windows stays installed as it is, with the steps done so far.",
    },
  ])("says what stopping a run in $phase leaves behind", async ({ phase, consequence }) => {
    const current = run({ phase });
    renderAt(
      `/machines/${machineId}`,
      standardAnswers([machine({ deployment: current })], [current], [view({ summary: current })]),
    );

    fireEvent.click(await screen.findByRole("button", { name: "Stop" }));
    const dialog = await screen.findByRole("dialog", { name: "Stop the deployment?" });

    expect(within(dialog).getByText(consequence)).toBeInTheDocument();
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
    renderAt(
      `/machines/${machineId}`,
      standardAnswers(
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
    renderAt(
      `/machines/${machineId}`,
      standardAnswers([machine({ state: "Approved", deployment: null })], [], []),
    );

    expect(
      await screen.findByText(
        "This machine has not run a task sequence yet. Assign one above, or add a rule for its model on the Rules page.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Run" })).not.toBeInTheDocument();
  });

  it("says when the machine was removed", async () => {
    renderAt(`/machines/${machineId}`, standardAnswers([], [], []));

    expect(
      await screen.findByText(
        "This machine was removed. It registers as a new machine at its next netboot.",
      ),
    ).toBeInTheDocument();
  });
});
