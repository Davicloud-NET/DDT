// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, QueryClientProvider, queryOptions } from "@tanstack/react-query";
import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { FLASH_MS } from "@/ui/motion";

import { useLiveMarks } from "./useLiveMarks";

interface Step {
  id: string;
  state: "Pending" | "Running" | "Done" | "Failed";
  percent: number;
}

function stepsQuery(run: string) {
  return queryOptions({
    queryKey: ["test-steps", run],
    queryFn: () => Promise.resolve<Step[]>([]),
  });
}

// A list of a run's steps, marked the way a page marks them. A step that finishes flashes. One that starts, or
// only changes its progress, doesn't.
function Steps({ run, ids }: { run: string; ids: string[] }) {
  const mark = useLiveMarks({
    queryKey: stepsQuery(run).queryKey,
    items: (steps) => steps,
    id: (step) => step.id,
    signature: (step) => step.state,
    tone: (step) => (step.state === "Done" ? "ok" : step.state === "Failed" ? "fail" : null),
  });

  return (
    <ul>
      {ids.map((id) => (
        <li key={id} data-testid={id} className={mark(id)}>
          {id}
        </li>
      ))}
    </ul>
  );
}

function show(steps: Step[], ids = steps.map((step) => step.id)) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(stepsQuery("r1").queryKey, steps);

  const view = render(
    <QueryClientProvider client={queryClient}>
      <Steps run="r1" ids={ids} />
    </QueryClientProvider>,
  );

  // Patches the cache as the hub's events and the answers of actions do.
  const push = (next: Step[], run = "r1") => {
    act(() => {
      queryClient.setQueryData(stepsQuery(run).queryKey, next);
    });
  };

  // Reads the list from the server, as a first load, a reconnect or polling does.
  const readAgain = async (next: Step[]) => {
    await act(() =>
      queryClient.query({ ...stepsQuery("r1"), queryFn: () => Promise.resolve(next) }),
    );
  };

  return { queryClient, view, push, readAgain };
}

const running: Step = { id: "s1", state: "Running", percent: 40 };
const waiting: Step = { id: "s2", state: "Pending", percent: 0 };

describe("marking what the hub changed", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("marks nothing on the first render", () => {
    show([running, waiting]);

    expect(screen.getByTestId("s1").className).toBe("");
    expect(screen.getByTestId("s2").className).toBe("");
  });

  it("flashes an item whose state a change pushed, in its new state's colour, until the flash has run", () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    const { push } = show([running, waiting]);

    push([{ ...running, state: "Done", percent: 100 }, waiting]);

    const done = screen.getByTestId("s1");
    expect(done).toHaveClass("live-flash", "live-tone-ok");
    expect(done).not.toHaveClass("live-new");
    expect(screen.getByTestId("s2").className).toBe("");

    act(() => {
      vi.advanceTimersByTime(FLASH_MS);
    });

    expect(screen.getByTestId("s1").className).toBe("");
  });

  it("does not flash what only changed outside the signature, or changed to a state without a colour", () => {
    const { push } = show([running, waiting]);

    push([{ ...running, percent: 80 }, waiting]);
    push([
      { ...running, percent: 80 },
      { ...waiting, state: "Running" },
    ]);

    expect(screen.getByTestId("s1").className).toBe("");
    expect(screen.getByTestId("s2").className).toBe("");
  });

  it("does not flash what the list read, as after a reconnect", async () => {
    const { readAgain } = show([running, waiting]);

    await readAgain([
      { ...running, state: "Failed" },
      { ...waiting, state: "Done", percent: 100 },
    ]);

    expect(screen.getByTestId("s1").className).toBe("");
    expect(screen.getByTestId("s2").className).toBe("");
  });

  it("marks changes pushed after a read against what was read", async () => {
    const { push, readAgain } = show([running, waiting]);

    await readAgain([{ ...running, state: "Failed" }, waiting]);
    push([
      { ...running, state: "Failed" },
      { ...waiting, state: "Done" },
    ]);

    expect(screen.getByTestId("s1").className).toBe("");
    expect(screen.getByTestId("s2")).toHaveClass("live-flash", "live-tone-ok");
  });

  it("lets an item that was not there before enter, flashing only where its state has a colour", () => {
    const { push } = show([running], ["s1", "s2", "s3"]);

    push([running, waiting, { id: "s3", state: "Failed", percent: 0 }]);

    expect(screen.getByTestId("s2")).toHaveClass("live-new");
    expect(screen.getByTestId("s2")).not.toHaveClass("live-flash");
    expect(screen.getByTestId("s3")).toHaveClass("live-new", "live-flash", "live-tone-fail");
  });

  it("starts the flash over when the same item changes again", () => {
    const { push } = show([running, waiting]);

    push([{ ...running, state: "Failed" }, waiting]);
    const first = screen.getByTestId("s1").className;
    push([{ ...running, state: "Done" }, waiting]);
    const second = screen.getByTestId("s1").className;

    expect(first).toContain("live-flash");
    expect(second).toContain("live-flash");
    const cycle = /live-cycle-\d/;
    expect(cycle.exec(first)?.[0]).not.toBe(cycle.exec(second)?.[0]);
  });

  it("follows the query of its key only, and starts over when the key changes", () => {
    const { queryClient, view, push } = show([running, waiting]);

    push([{ ...running, state: "Done" }, waiting], "r2");
    expect(screen.getByTestId("s1").className).toBe("");

    push([{ ...running, state: "Failed" }, waiting]);
    expect(screen.getByTestId("s1")).toHaveClass("live-flash");

    // Another run's list starts from what its query holds. It marks nothing from the previous run's list.
    view.rerender(
      <QueryClientProvider client={queryClient}>
        <Steps run="r2" ids={["s1", "s2"]} />
      </QueryClientProvider>,
    );

    expect(screen.getByTestId("s1").className).toBe("");
  });
});
