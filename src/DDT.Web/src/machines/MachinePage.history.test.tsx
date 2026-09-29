// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { nth, press } from "@/test/aria";

import {
  answers,
  history,
  machine,
  machineId,
  newerRunId,
  now,
  olderRunId,
  open,
  run,
  runId,
  step,
  view,
} from "./MachinePage.fixtures";

describe("MachinePage", () => {
  afterEach(() => {
    vi.useRealTimers();
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

    // While the connection is down, the list is still read on a timer, like every list. The ended run isn't read again.
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
});
