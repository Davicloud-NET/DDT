// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { QueryClient } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, onTestFinished, vi } from "vitest";

import type { DeploymentStepView } from "@/deployments/deployments";
import { LiveContext } from "@/live/LiveContext";
import { fill, press } from "@/test/aria";
import { logLine, stepView } from "@/test/builders";
import { testHub, type TestHub } from "@/test/fakeHub";
import { settle } from "@/test/settle";

import type { MachineLogEntry } from "./log";
import { LogPanel } from "./LogPanel";

const start = Date.parse("2026-09-16T10:00:00Z");

function range(from: number, to: number): MachineLogEntry[] {
  return Array.from({ length: to - from + 1 }, (_, index) => logLine(from + index));
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
  onTestFinished(() => {
    vi.unstubAllGlobals();
  });

  return { reads };
}

// A live connection that is up, whose hub the test drives.
async function connected(): Promise<TestHub> {
  const hub = testHub(new QueryClient());

  await act(async () => {
    hub.live.start();
    await settle();
  });
  onTestFinished(() => {
    hub.live.stop();
  });

  return hub;
}

function renderPanel(
  hub: TestHub | null,
  props: { stepFilter?: string | null; steps?: DeploymentStepView[]; active?: boolean } = {},
) {
  const onStepFilterChange = vi.fn();

  render(
    <I18nProvider i18n={i18n}>
      <LiveContext value={hub?.live ?? null}>
        <LogPanel
          machineId="m1"
          runId="d1"
          active={props.active ?? true}
          steps={props.steps ?? []}
          stepFilter={props.stepFilter ?? null}
          onStepFilterChange={onStepFilterChange}
        />
      </LiveContext>
    </I18nProvider>,
  );

  return { onStepFilterChange };
}

function viewport(): HTMLElement {
  return screen.getByRole("log", { name: "Log lines" });
}

// jsdom has no layout, so the test says how tall the log and its view are.
function layOut(element: HTMLElement, scrollHeight: number, clientHeight: number) {
  Object.defineProperty(element, "scrollHeight", { configurable: true, value: scrollHeight });
  Object.defineProperty(element, "clientHeight", { configurable: true, value: clientHeight });
}

function scrollTo(element: HTMLElement, top: number) {
  element.scrollTop = top;
  fireEvent.scroll(element);
}

describe("LogPanel", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("loads the newest lines of the run, then reads the lines the server announces", async () => {
    const stored = range(1, 3);
    const { reads } = logServer(stored);
    const hub = await connected();
    renderPanel(hub);

    expect(await screen.findByText("Line 3")).toBeInTheDocument();
    expect(reads[0]).toBe("?limit=500&deploymentId=d1");
    expect(hub.invocations).toContain("WatchMachine m1");
    expect(screen.queryByText(/live connection/)).not.toBeInTheDocument();

    stored.push(logLine(4), logLine(5));
    act(() => {
      hub.push("machineLogAppended", { machineId: "m1", lastLineId: 5 });
    });

    expect(await screen.findByText("Line 5")).toBeInTheDocument();
    expect(reads).toContain("?after=3&limit=500&deploymentId=d1");

    // Lines sent while the connection was down are read once it is back.
    stored.push(logLine(6));
    act(() => {
      hub.loseConnection();
      hub.reconnect();
    });

    expect(await screen.findByText("Line 6")).toBeInTheDocument();
    expect(reads).toContain("?after=5&limit=500&deploymentId=d1");
  });

  it("does not read again for a line it already has", async () => {
    const { reads } = logServer(range(1, 3));
    const hub = await connected();
    renderPanel(hub);

    await screen.findByText("Line 3");
    act(() => {
      hub.push("machineLogAppended", { machineId: "m1", lastLineId: 3 });
      hub.push("machineLogAppended", { machineId: "m2", lastLineId: 9 });
    });
    await settle();

    expect(reads).toEqual(["?limit=500&deploymentId=d1"]);
  });

  it("pauses following when scrolled up, counts the new lines and follows again on request", async () => {
    const stored = range(1, 3);
    logServer(stored);
    const hub = await connected();
    renderPanel(hub);

    await screen.findByText("Line 3");
    const log = viewport();
    layOut(log, 2000, 200);
    scrollTo(log, 100);

    expect(await screen.findByRole("status")).toHaveTextContent("Paused");

    stored.push(logLine(4), logLine(5));
    act(() => {
      hub.push("machineLogAppended", { machineId: "m1", lastLineId: 5 });
    });

    await screen.findByText("Line 5");
    expect(screen.getByRole("status")).toHaveTextContent("Paused, 2 new lines");
    expect(log.scrollTop).toBe(100);

    press(screen.getByRole("button", { name: "Jump to the newest" }));

    await waitFor(() => {
      expect(screen.queryByRole("status")).not.toBeInTheDocument();
    });
    expect(log.scrollTop).toBe(2000);
  });

  it("follows again once scrolled back to the newest line", async () => {
    logServer(range(1, 3));
    renderPanel(await connected());

    await screen.findByText("Line 3");
    const log = viewport();
    layOut(log, 2000, 200);
    scrollTo(log, 100);
    expect(await screen.findByRole("status")).toHaveTextContent("Paused");

    scrollTo(log, 1800);

    await waitFor(() => {
      expect(screen.queryByRole("status")).not.toBeInTheDocument();
    });
  });

  it("loads older lines without moving the lines in view", async () => {
    logServer(range(1, 700));
    renderPanel(await connected());

    await screen.findByText("Line 700");
    expect(
      screen.getByRole("searchbox", { name: "Search the 500 loaded lines" }),
    ).toBeInTheDocument();
    const log = viewport();
    layOut(log, 10_000, 200);
    scrollTo(log, 400);
    await screen.findByRole("status");

    press(screen.getByRole("button", { name: "Load older lines" }));

    expect(
      await screen.findByText(
        `Start of the stored log. The server keeps the newest ${(50_000).toLocaleString("en")} lines of each machine.`,
      ),
    ).toBeInTheDocument();
    // 200 older lines of 20 px each now sit above the lines that were in view.
    expect(log.scrollTop).toBe(400 + 200 * 20);
    expect(
      screen.getByRole("searchbox", { name: "Search the 700 loaded lines" }),
    ).toBeInTheDocument();
  });

  it("shows the agent's own clock when it was off by more than 2 s", async () => {
    const skewed = logLine(1, {
      agentTimestampUtc: new Date(start + 1000 - 7_200_000).toISOString(),
    });
    const close = logLine(2, { agentTimestampUtc: new Date(start + 2000 - 1_500).toISOString() });
    logServer([skewed, close]);
    renderPanel(await connected());

    const skewedRow = (await screen.findByText("Line 1")).closest("button");
    const closeRow = screen.getByText("Line 2").closest("button");

    expect(skewedRow?.querySelector("time")).toHaveAttribute(
      "title",
      `The agent's clock said ${new Date(skewed.agentTimestampUtc).toLocaleTimeString("en")}, 2 h 0 min behind the server.`,
    );
    expect(closeRow?.querySelector("time")).not.toHaveAttribute("title");
  });

  it("reads every 5 s while there is no live connection and the run is active", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] });
    const stored = range(1, 2);
    logServer(stored);
    renderPanel(null);

    await screen.findByText("Line 2");
    expect(
      screen.getByText("Reading every 5 s, because the live connection is down."),
    ).toBeInTheDocument();

    stored.push(logLine(3));
    act(() => {
      vi.advanceTimersByTime(5_000);
    });

    expect(await screen.findByText("Line 3")).toBeInTheDocument();
  });

  it("waits for the connection to read the lines of a run that ended", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] });
    const stored = range(1, 2);
    const { reads } = logServer(stored);
    const hub = await connected();
    renderPanel(hub, { active: false });

    await screen.findByText("Line 2");
    act(() => {
      hub.loseConnection();
    });

    expect(
      await screen.findByText(
        "Reconnecting. Lines sent meanwhile are read once the connection is back.",
      ),
    ).toBeInTheDocument();
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(reads).toHaveLength(1);
  });

  it("reads the whole machine's log when asked, and says when there is none", async () => {
    const { reads } = logServer([logLine(1, { deploymentId: "d0", message: "An earlier run" })]);
    renderPanel(await connected());

    expect(
      await screen.findByText(
        "No log lines for this run yet. The agent sends its log while it runs.",
      ),
    ).toBeInTheDocument();

    press(
      within(screen.getByRole("radiogroup", { name: "Which lines" })).getByRole("radio", {
        name: "Whole machine",
      }),
    );

    expect(await screen.findByText("An earlier run")).toBeInTheDocument();
    expect(reads).toContain("?limit=500");
  });

  it("filters the loaded lines by level, text and step, and shows a message in full", async () => {
    logServer([
      logLine(1, { stepId: "s1", message: "Partitioning disk 0" }),
      logLine(2, { stepId: "s2", level: "Warning", message: "No driver package matches" }),
      logLine(3, { stepId: "s2", level: "Error", message: "Applying failed\nat index 1\nat W:" }),
    ]);
    const steps = [
      stepView({
        stepId: "s2",
        index: 1,
        name: "Apply Windows 11",
        kind: "applyImage",
        state: "Failed",
        percent: 10,
      }),
    ];
    const { onStepFilterChange } = renderPanel(await connected(), { stepFilter: "s2", steps });

    expect(await screen.findByText("No driver package matches")).toBeInTheDocument();
    expect(screen.queryByText("Partitioning disk 0")).not.toBeInTheDocument();
    expect(screen.getByText(/Only the lines of step 2, Apply Windows 11\./)).toBeInTheDocument();

    const levels = screen.getByRole("toolbar", { name: "Levels" });
    press(within(levels).getByRole("button", { name: /^Warnings/ }));

    expect(screen.queryByText("No driver package matches")).not.toBeInTheDocument();
    expect(screen.getByText("(+2 lines)")).toBeInTheDocument();

    const search = screen.getByRole("searchbox", { name: "Search the 3 loaded lines" });
    fill(search, "nothing like it");

    expect(screen.getByText("No loaded line matches the filter.")).toBeInTheDocument();

    fill(search, "applying");
    press(screen.getByText("Applying failed"));

    const detail = screen.getByRole("region", { name: "Log line" });
    expect(within(detail).getByText(/at index 1/)).toHaveTextContent(
      "Applying failed at index 1 at W:",
    );
    expect(detail).toHaveTextContent("during step 2, Apply Windows 11");

    press(within(detail).getByRole("button", { name: "Close the line" }));
    expect(screen.queryByRole("region", { name: "Log line" })).not.toBeInTheDocument();

    press(screen.getByRole("button", { name: "Show every step" }));

    expect(onStepFilterChange).toHaveBeenCalledWith(null);
  });
});
