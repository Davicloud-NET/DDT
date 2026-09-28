// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { DeploymentView } from "@/deployments/deployments";
import { press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { logLine, operator, sequenceResolution, viewer } from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import type { Routes } from "@/test/server";
import { toasts } from "@/ui/toasts";
import {
  node,
  pauseMessage,
  treeMachine,
  treeMachineId,
  treeRunId,
  treeRunSummary,
  treeRunView,
  treeSteps,
} from "@/test/treeRun";

// A machine whose run goes through a tree and waits at a pause, on its page: the flow with the path it took, what each
// node decided, the notice that lets the run go on, the answers a run can wait for, its values and the machine's
// facts, all kept live.

const now = new Date("2026-09-16T10:06:00Z");

function routes(view: DeploymentView = treeRunView(), extra: Routes = {}): Routes {
  return {
    "GET /api/machines": { body: [treeMachine({ deployment: view.summary })] },
    [`GET /api/machines/${treeMachineId}/deployments`]: { body: [view.summary] },
    [`GET /api/machines/${treeMachineId}/sequence`]: {
      body: sequenceResolution({
        source: "Assigned",
        explanation:
          "anna assigned Windows 11 office PCs on the web, which comes before every rule.",
      }),
    },
    [`GET /api/deployments/${treeRunId}`]: { body: view },
    [`GET /api/machines/${treeMachineId}/log?limit=500&deploymentId=${treeRunId}`]: {
      body: {
        lines: [
          logLine(1, {
            deploymentId: treeRunId,
            stepId: node.partition,
            message: "Partitioned disk 0",
          }),
          logLine(2, {
            deploymentId: treeRunId,
            stepId: node.pause,
            message: "Waiting for someone",
          }),
        ],
        hasOlder: false,
      },
    },
    ...extra,
  };
}

function open(extra: Routes = {}, user = operator, view?: DeploymentView): Promise<RenderedPage> {
  return renderPage({ path: `/machines/${treeMachineId}`, user, routes: routes(view, extra) });
}

function region(name: string): HTMLElement {
  return screen.getByRole("region", { name });
}

// A panel by its heading.
function panel(heading: string): HTMLElement {
  const found = screen.getByRole("heading", { name: heading }).closest("section");

  if (found === null) {
    throw new Error(`${heading} is not a panel.`);
  }

  return found;
}

function stepsList(): HTMLElement {
  return screen.getByRole("list", { name: "Steps" });
}

function stepRow(name: string): HTMLElement {
  const item = within(stepsList()).getByText(name, { selector: "span.type-label" }).closest("li");

  if (item === null) {
    throw new Error(`${name} is not a step.`);
  }

  return item;
}

function flowNode(name: RegExp): HTMLElement {
  return within(screen.getByRole("group", { name: "Flow of this run" })).getByRole("button", {
    name,
  });
}

// What the pause answered with: the pause step done, and the run going on.
function continued(): DeploymentView {
  const summary = treeRunSummary({
    activity: "Step",
    waiting: false,
    pauseMessage: null,
    stepIndex: 14,
    stepName: "Restart",
    updatedUtc: "2026-09-16T10:06:05Z",
  });

  return treeRunView({
    summary,
    pause: null,
    steps: treeSteps.map((step) =>
      step.stepId === node.pause
        ? { ...step, state: "Done", finishedUtc: "2026-09-16T10:06:05Z" }
        : step.stepId === node.restart
          ? { ...step, state: "Running", pass: 1, startedUtc: "2026-09-16T10:06:05Z" }
          : step,
    ),
  });
}

describe("a machine's page with a tree run", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("shows the path the run took, what each node decided, the values and the machine's facts", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now });
    await open();

    expect(
      await screen.findByRole("heading", { level: 1, name: "PC-G2341KXQ" }),
    ).toBeInTheDocument();
    const waits = await screen.findByRole("region", { name: "The run waits" });
    expect(waits).toHaveTextContent(`Paused at 11, Check the asset tag. ${pauseMessage}`);

    // The rail counts the path, and leaves out the step of the branch not taken.
    expect(await screen.findByText(/steps on this path/)).toHaveTextContent(
      "10 of 11 steps on this path",
    );
    expect(screen.getByText("Step 11, Check the asset tag, paused")).toBeInTheDocument();
    expect(screen.getByText("Step 12, Restart, not started")).toBeInTheDocument();
    expect(screen.queryByText(/^Step 4, /)).not.toBeInTheDocument();

    // The flow draws every node, the one not taken as such, and shows the paused one first.
    expect(await screen.findByRole("group", { name: "Flow of this run" })).toBeInTheDocument();
    expect(flowNode(/Apply Windows 11, Not taken$/)).toBeInTheDocument();
    expect(flowNode(/Check the asset tag, Paused$/)).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("button", { name: "Follow the run" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    const chosen = region("The chosen step");
    expect(
      within(chosen).getByRole("heading", { name: "Check the asset tag" }),
    ).toBeInTheDocument();
    expect(chosen).toHaveTextContent("Paused since");

    // The IF's decision, as the agent recorded it.
    press(flowNode(/If: Is it a Latitude\?, Done$/));
    await waitFor(() => {
      expect(within(region("The chosen step")).getByRole("heading")).toHaveTextContent(
        "If: Is it a Latitude?",
      );
    });
    const decision = region("The chosen step");
    expect(decision).toHaveTextContent("Took Then");
    expect(decision).toHaveTextContent("Model contains Latitude");
    expect(within(decision).getByText("Holds")).toBeInTheDocument();
    expect(within(decision).getByText("The machine reported Latitude 7450.")).toBeInTheDocument();

    // The steps of the path, with where they sit and what they decided; the step not taken is not among them.
    const steps = within(stepsList());
    expect(
      within(stepRow("If: Is it a Latitude?")).getByText(
        'Took Then: Model contains Latitude holds for "Latitude 7450"',
      ),
    ).toBeInTheDocument();
    expect(
      within(stepRow("Install the site printer")).getByText(
        'Skipped: Device kind is Desktop does not hold for "Laptop"',
      ),
    ).toBeInTheDocument();
    expect(
      within(stepRow("Install the site printer")).getAllByText("Group: Berlin office"),
    ).not.toHaveLength(0);
    expect(
      within(stepRow("Repeat: Wait for the share")).getByText(
        'Stopped after 2 of at most 5 times: Last step failed is no holds for "no"',
      ),
    ).toBeInTheDocument();
    expect(
      within(stepRow("Test the share")).getByText("Ran 2 times, the last is shown."),
    ).toBeInTheDocument();
    expect(steps.queryByText("Apply Windows 11")).not.toBeInTheDocument();

    // The values of the run with their sources; a secret only says it was given.
    const values = panel("Values of this run");
    expect(values).toHaveTextContent("ComputerNamePC-G2341KXQSet by step 5, Name the computer");
    expect(values).toHaveTextContent("TimeZoneW. Europe Standard TimeFrom the rule Berlin office");
    expect(values).toHaveTextContent("DepartmentSalesAnswered by anna for this run");
    expect(values).toHaveTextContent("JoinAccountGiven, never shown");
    expect(values).not.toHaveTextContent("UTC");

    // What the machine reported.
    const facts = panel("Machine facts");
    expect(
      within(facts).getByText("Memory", { selector: "dt" }).nextElementSibling,
    ).toHaveTextContent("16 GB");
    expect(
      within(facts).getByText("Asset tag", { selector: "dt" }).nextElementSibling,
    ).toHaveTextContent("INV-40211");
    expect(
      within(facts).getByText("IPv4 address", { selector: "dt" }).nextElementSibling,
    ).toHaveTextContent("10.20.4.51/24");

    await expectNoAxeViolations();
  });

  it("lets an operator continue the paused run and takes in the server's answer", async () => {
    const { server } = await open({
      [`POST /api/machines/${treeMachineId}/deployments/current/continue`]: { body: continued() },
    });

    const waits = await screen.findByRole("region", { name: "The run waits" });
    const key = within(waits).getByRole("button", { name: "Continue the run" });
    await waitFor(() => {
      expect(key).toBeEnabled();
    });
    press(key);

    await waitFor(() => {
      expect(screen.queryByRole("region", { name: "The run waits" })).not.toBeInTheDocument();
    });
    expect(server.changes().map((request) => [request.path, request.body])).toEqual([
      [
        `/api/machines/${treeMachineId}/deployments/current/continue`,
        { stepId: node.pause, pass: 1 },
      ],
    ]);
    expect(within(stepRow("Check the asset tag")).getByText("Done")).toBeInTheDocument();
    expect(server.count("GET /api/machines")).toBe(1);
    expect(server.count(`GET /api/deployments/${treeRunId}`)).toBe(1);
  });

  it("says so when the run went on before the operator's continue arrived", async () => {
    await open({
      [`POST /api/machines/${treeMachineId}/deployments/current/continue`]: {
        status: 409,
        body: continued(),
      },
    });

    const waits = await screen.findByRole("region", { name: "The run waits" });
    const key = within(waits).getByRole("button", { name: "Continue the run" });
    await waitFor(() => {
      expect(key).toBeEnabled();
    });
    press(key);

    await waitFor(() => {
      expect(toasts.visibleToasts.map((toast) => toast.content.title)).toContain(
        "The run went on already",
      );
    });
    toasts.clear();
    await waitFor(() => {
      expect(screen.queryByRole("region", { name: "The run waits" })).not.toBeInTheDocument();
    });
  });

  it("shows a viewer the pause without a key to continue it", async () => {
    await open({}, viewer);

    const waits = await screen.findByRole("region", { name: "The run waits" });
    expect(waits).toHaveTextContent("The person at the machine can let the run go on there, too.");
    expect(within(waits).queryByRole("button")).not.toBeInTheDocument();
  });

  it("asks the answers a run waits for at its start, and sends them", async () => {
    const summary = treeRunSummary({
      activity: "WaitingForInput",
      waiting: true,
      pauseMessage: null,
      stepIndex: null,
      stepName: null,
    });
    const waiting = treeRunView({
      summary,
      pause: null,
      steps: treeSteps.map((step) => ({
        ...step,
        state: "Pending",
        pass: 0,
        startedUtc: null,
        finishedUtc: null,
        branch: null,
        evaluation: null,
        iteration: 0,
      })),
      inputs: [
        {
          input: {
            name: "Department",
            label: "Department",
            help: null,
            kind: "Choice",
            choices: [
              { value: "Sales", label: null },
              { value: "Finance", label: null },
            ],
            default: null,
            required: true,
            maxLength: null,
          },
          answered: false,
          answeredBy: null,
        },
        {
          input: {
            name: "Room",
            label: "Room",
            help: null,
            kind: "Text",
            choices: [],
            default: null,
            required: false,
            maxLength: 12,
          },
          answered: true,
          answeredBy: "anna",
        },
      ],
    });
    const answered = treeRunView({
      summary: treeRunSummary({
        activity: "Preparing",
        waiting: false,
        pauseMessage: null,
        updatedUtc: "2026-09-16T10:06:05Z",
      }),
      pause: null,
      steps: waiting.steps,
    });
    const { server } = await open(
      { [`POST /api/machines/${treeMachineId}/deployments/current/answers`]: { body: answered } },
      operator,
      waiting,
    );

    const waits = await screen.findByRole("region", { name: "The run waits" });
    await waitFor(() => {
      expect(waits).toHaveTextContent(
        "The run waits for answers before it starts. It asks for Department.",
      );
    });
    const key = within(waits).getByRole("button", { name: "Give the answers" });
    await waitFor(() => {
      expect(key).toBeEnabled();
    });
    press(key);

    const dialog = await screen.findByRole("dialog", { name: "Answers for Windows 11 office PCs" });
    expect(within(dialog).queryByRole("textbox", { name: "Room" })).not.toBeInTheDocument();
    press(within(dialog).getByRole("radio", { name: "Finance" }));
    await expectNoAxeViolations();
    press(within(dialog).getByRole("button", { name: "Give the answers" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    await waitFor(() => {
      expect(screen.queryByRole("region", { name: "The run waits" })).not.toBeInTheDocument();
    });
    expect(server.changes().map((request) => request.body)).toEqual([
      { answers: [{ name: "Department", value: "Finance" }] },
    ]);
  });

  it("keeps the flow, the steps and the values live", async () => {
    const { hub } = await open();

    await screen.findByRole("list", { name: "Steps" });
    await screen.findByRole("group", { name: "Flow of this run" });

    act(() => {
      hub?.push("runVariablesChanged", {
        machineId: treeMachineId,
        deploymentId: treeRunId,
        variables: { ComputerName: "PC-G2341KXQ", Office: "Berlin 2" },
      });
    });
    await waitFor(() => {
      expect(panel("Values of this run")).toHaveTextContent(
        "OfficeBerlin 2Set by a step while the run went on",
      );
    });

    // The run goes on: the pause is done, the restart runs, the machine no longer waits.
    const next = continued();
    act(() => {
      hub?.push("runStepChanged", {
        machineId: treeMachineId,
        deploymentId: treeRunId,
        step: next.steps.find((step) => step.stepId === node.pause),
      });
      hub?.push("runStepChanged", {
        machineId: treeMachineId,
        deploymentId: treeRunId,
        step: next.steps.find((step) => step.stepId === node.restart),
      });
      hub?.push("machineChanged", treeMachine({ deployment: next.summary }));
    });

    await waitFor(() => {
      expect(screen.queryByRole("region", { name: "The run waits" })).not.toBeInTheDocument();
    });
    expect(within(stepRow("Restart")).getByText("Running 0%")).toBeInTheDocument();
    expect(flowNode(/Restart, Running$/)).toBeInTheDocument();
    expect(screen.getByText("Step 11, Check the asset tag, done")).toBeInTheDocument();

    // A later pass of a node inside the repeat replaces the one shown.
    act(() => {
      hub?.push("runStepChanged", {
        machineId: treeMachineId,
        deploymentId: treeRunId,
        step: { ...treeSteps[12], state: "Running", pass: 3, percent: 10, finishedUtc: null },
      });
    });
    await waitFor(() => {
      expect(
        within(stepRow("Test the share")).getByText("Ran 3 times, the last is shown."),
      ).toBeInTheDocument();
    });

    // Waiting again, the machine says so at once.
    act(() => {
      hub?.push(
        "machineChanged",
        treeMachine({ deployment: treeRunSummary({ updatedUtc: "2026-09-16T10:07:00Z" }) }),
      );
    });
    expect(await screen.findByRole("region", { name: "The run waits" })).toBeInTheDocument();
  });

  it("follows the run until the person moves the flow, and goes from node to node with the keys", async () => {
    await open();

    const follow = await screen.findByRole("button", { name: "Follow the run" });
    expect(follow).toHaveAttribute("aria-pressed", "true");

    press(screen.getByRole("button", { name: "Zoom in" }));
    await waitFor(() => {
      expect(follow).toHaveAttribute("aria-pressed", "false");
    });
    press(follow);
    await waitFor(() => {
      expect(follow).toHaveAttribute("aria-pressed", "true");
    });

    // One stop of the Tab key, on the node chosen; Up goes back the way the flow runs, into the repeat.
    const paused = flowNode(/Check the asset tag, Paused$/);
    expect(paused).toHaveAttribute("tabindex", "0");
    paused.focus();
    fireEvent.keyDown(paused, { key: "ArrowUp" });

    await waitFor(() => {
      expect(within(region("The chosen step")).getByRole("heading")).toHaveTextContent(
        "Test the share",
      );
    });
    expect(flowNode(/Test the share, Done$/)).toHaveFocus();
    expect(region("The chosen step")).toHaveTextContent("Repeat: Wait for the share");
    expect(region("The chosen step")).toHaveTextContent("Ran 2 times, the last is shown.");
  });

  it("shows the log of a step chosen in the flow", async () => {
    await open();

    expect(await screen.findByText("Waiting for someone")).toBeInTheDocument();
    press(flowNode(/Partition the disk, Done$/));
    const chosen = region("The chosen step");
    await waitFor(() => {
      expect(within(chosen).getByRole("heading")).toHaveTextContent("Partition the disk");
    });
    press(
      within(region("The chosen step")).getByRole("button", { name: "Show the log of this step" }),
    );

    expect(
      await screen.findByText(/^Only the lines of step 1, Partition the disk\./),
    ).toBeInTheDocument();
    expect(screen.getByText("Partitioned disk 0")).toBeInTheDocument();
    expect(screen.queryByText("Waiting for someone")).not.toBeInTheDocument();
  });

  it("marks the machine as paused in the list", async () => {
    await renderPage({ path: "/machines", user: operator, routes: routes() });

    const row = (await screen.findByRole("link", { name: "PC-G2341KXQ" })).closest("tr");

    expect(row).not.toBeNull();
    // Its state, and what its run does.
    expect(within(row as HTMLElement).getAllByText("Paused")).toHaveLength(2);
  });
});
