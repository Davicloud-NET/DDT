// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { DeploymentStepView } from "@/deployments/deployments";
import type { LiveConnection, MachineWatchHandlers } from "@/live/liveConnection";
import { LiveContext } from "@/live/LiveContext";

import type { MachineLogEntry } from "./log";
import { LogPanel } from "./LogPanel";

const start = Date.parse("2026-09-16T10:00:00Z");

function line(id: number, more: Partial<MachineLogEntry> = {}): MachineLogEntry {
  const time = new Date(start + id * 1000).toISOString();

  return {
    id,
    timestampUtc: time,
    receivedUtc: time,
    level: "Information",
    message: `Line ${String(id)}`,
    agentTimestampUtc: time,
    deploymentId: "d1",
    stepId: null,
    ...more,
  };
}

function range(from: number, to: number): MachineLogEntry[] {
  return Array.from({ length: to - from + 1 }, (_, index) => line(from + index));
}

// Answers log reads as the server does, from the lines the test puts here.
function logServer(stored: MachineLogEntry[]) {
  const reads: string[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL) => {
      const url = new URL(input instanceof Request ? input.url : input.toString(), "http://host");
      reads.push(url.search);

      const number = (name: string) => {
        const value = url.searchParams.get(name);
        return value === null ? null : Number(value);
      };
      const after = number("after");
      const before = number("before");
      const limit = number("limit") ?? 500;
      const run = url.searchParams.get("deploymentId");
      const matching = stored.filter((entry) => run === null || entry.deploymentId === run);
      const window = matching.filter(
        (entry) => (after === null || entry.id > after) && (before === null || entry.id < before),
      );
      const page = after === null ? window.slice(-limit) : window.slice(0, limit);
      const boundary = page[0]?.id ?? (after === null ? (before ?? Infinity) : after + 1);

      return Promise.resolve(
        new Response(
          JSON.stringify({ lines: page, hasOlder: matching.some((entry) => entry.id < boundary) }),
          { status: 200 },
        ),
      );
    }),
  );

  return { reads };
}

// A live connection that is up and hands the panel's watch to the test.
function fakeLive() {
  let watch: MachineWatchHandlers | null = null;
  const live: LiveConnection = {
    start: () => undefined,
    stop: () => undefined,
    status: () => "live",
    onStatusChange: () => () => undefined,
    watchMachine: (_, handlers) => {
      watch = handlers;

      return () => {
        watch = null;
      };
    },
  };

  return {
    live,
    watcher: () => {
      if (watch === null) {
        throw new Error("Nothing watches the machine.");
      }

      return watch;
    },
  };
}

function renderPanel(
  live: LiveConnection | null,
  props: { stepFilter?: string | null; steps?: DeploymentStepView[] } = {},
) {
  const onStepFilterChange = vi.fn();
  const panel = (
    <LogPanel
      machineId="m1"
      runId="d1"
      active
      steps={props.steps ?? []}
      stepFilter={props.stepFilter ?? null}
      onStepFilterChange={onStepFilterChange}
    />
  );
  const wrap = (children: ReactNode) =>
    live === null ? children : <LiveContext value={live}>{children}</LiveContext>;

  render(wrap(panel));

  return { onStepFilterChange };
}

// jsdom has no layout, so the test says how tall the log and its view are.
function layOut(viewport: HTMLElement, scrollHeight: number, clientHeight: number) {
  Object.defineProperty(viewport, "scrollHeight", { configurable: true, value: scrollHeight });
  Object.defineProperty(viewport, "clientHeight", { configurable: true, value: clientHeight });
}

describe("LogPanel", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("loads the newest lines of the run, then reads the lines the server announces", async () => {
    const stored = range(1, 3);
    const { reads } = logServer(stored);
    const { live, watcher } = fakeLive();
    renderPanel(live);

    expect(await screen.findByText("Line 3")).toBeInTheDocument();
    expect(screen.getByText("New lines appear as the agent sends them.")).toBeInTheDocument();
    expect(reads[0]).toBe("?limit=500&deploymentId=d1");

    stored.push(line(4), line(5));
    act(() => {
      watcher().onLogAppended?.({ machineId: "m1", lastLineId: 5 });
    });

    expect(await screen.findByText("Line 5")).toBeInTheDocument();
    expect(reads).toContain("?after=3&limit=500&deploymentId=d1");

    // Lines sent while the connection was down are read once it is back.
    stored.push(line(6));
    act(() => {
      watcher().onReconnect?.();
    });

    expect(await screen.findByText("Line 6")).toBeInTheDocument();
    expect(reads).toContain("?after=5&limit=500&deploymentId=d1");
  });

  it("pauses following when scrolled up, counts the new lines and follows again on request", async () => {
    const stored = range(1, 3);
    logServer(stored);
    const { live, watcher } = fakeLive();
    renderPanel(live);

    await screen.findByText("Line 3");
    const viewport = screen.getByRole("log");
    layOut(viewport, 2000, 200);
    viewport.scrollTop = 100;
    fireEvent.scroll(viewport);

    expect(await screen.findByRole("status")).toHaveTextContent("Paused.");

    stored.push(line(4), line(5));
    act(() => {
      watcher().onLogAppended?.({ machineId: "m1", lastLineId: 5 });
    });

    await screen.findByText("Line 5");
    expect(screen.getByRole("status")).toHaveTextContent("Paused. 2 new lines.");
    expect(viewport.scrollTop).toBe(100);

    fireEvent.click(screen.getByRole("button", { name: "Jump to the newest" }));

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(viewport.scrollTop).toBe(2000);
  });

  it("loads older lines without moving the lines in view", async () => {
    logServer(range(1, 700));
    const { live } = fakeLive();
    renderPanel(live);

    await screen.findByText("Line 700");
    const viewport = screen.getByRole("log");
    layOut(viewport, 10_000, 200);
    viewport.scrollTop = 400;
    fireEvent.scroll(viewport);
    await screen.findByRole("status");

    fireEvent.click(screen.getByRole("button", { name: "Load older lines" }));

    expect(
      await screen.findByText(
        `Start of the stored log. The server keeps the newest ${(50_000).toLocaleString()} lines of each machine.`,
      ),
    ).toBeInTheDocument();
    // 200 older lines of 20 px each now sit above the lines that were in view.
    expect(viewport.scrollTop).toBe(400 + 200 * 20);
    expect(screen.getByLabelText("Search the 700 loaded lines")).toBeInTheDocument();
  });

  it("shows the agent's own clock when it was off by more than 2 s", async () => {
    const skewed = line(1, { agentTimestampUtc: new Date(start + 1000 - 7_200_000).toISOString() });
    const close = line(2, { agentTimestampUtc: new Date(start + 2000 - 1_500).toISOString() });
    logServer([skewed, close]);
    renderPanel(fakeLive().live);

    const skewedRow = (await screen.findByText("Line 1")).closest("button");
    const closeRow = screen.getByText("Line 2").closest("button");

    expect(skewedRow?.querySelector("time")).toHaveAttribute(
      "title",
      `The agent's clock said ${new Date(skewed.agentTimestampUtc).toLocaleTimeString()}, 2 h 0 min behind the server.`,
    );
    expect(closeRow?.querySelector("time")).not.toHaveAttribute("title");
  });

  it("reads every 5 s while there is no live connection", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] });
    const stored = range(1, 2);
    logServer(stored);
    renderPanel(null);

    await screen.findByText("Line 2");
    expect(
      screen.getByText("Polling every 5 s, because the live connection is down."),
    ).toBeInTheDocument();

    stored.push(line(3));
    act(() => {
      vi.advanceTimersByTime(5_000);
    });

    expect(await screen.findByText("Line 3")).toBeInTheDocument();
  });

  it("filters the loaded lines by level, text and step, and shows a message in full", async () => {
    logServer([
      line(1, { stepId: "s1", message: "Partitioning disk 0" }),
      line(2, { stepId: "s2", level: "Warning", message: "No driver package matches" }),
      line(3, { stepId: "s2", level: "Error", message: "Applying failed\nat index 1\nat W:" }),
    ]);
    const steps: DeploymentStepView[] = [
      {
        stepId: "s2",
        index: 1,
        name: "Apply Windows 11",
        kind: "applyImage",
        phase: "WindowsPE",
        state: "Failed",
        percent: 10,
        startedUtc: null,
        finishedUtc: null,
        error: null,
      },
    ];
    const { onStepFilterChange } = renderPanel(fakeLive().live, { stepFilter: "s2", steps });

    expect(await screen.findByText("No driver package matches")).toBeInTheDocument();
    expect(screen.queryByText("Partitioning disk 0")).not.toBeInTheDocument();
    expect(screen.getByText(/Only the lines of step 2, Apply Windows 11\./)).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText("Warning"));

    expect(screen.queryByText("No driver package matches")).not.toBeInTheDocument();
    expect(screen.getByText("(+2 lines)")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Search the 3 loaded lines"), {
      target: { value: "nothing like it" },
    });

    expect(screen.getByText("No loaded line matches the filter.")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Search the 3 loaded lines"), {
      target: { value: "applying" },
    });
    fireEvent.click(screen.getByText("Applying failed"));

    const detail = screen.getByRole("region", { name: "Log line" });
    expect(within(detail).getByText(/at index 1/)).toHaveTextContent(
      "Applying failed at index 1 at W:",
    );
    expect(detail).toHaveTextContent("during step 2, Apply Windows 11");

    fireEvent.click(screen.getByRole("button", { name: "Show every step" }));

    expect(onStepFilterChange).toHaveBeenCalledWith(null);
  });
});
