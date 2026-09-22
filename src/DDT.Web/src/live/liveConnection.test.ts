// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient } from "@tanstack/react-query";
import { act, renderHook } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { MachineSummary } from "@/machines/machines";

import { LiveContext } from "./LiveContext";
import {
  createLiveConnection,
  type LiveHub,
  type MachineLogAppended,
  type RunStepChanged,
} from "./liveConnection";
import { useMachineWatch } from "./useMachineWatch";

interface FakeHub extends LiveHub {
  starts: number;
  stopped: boolean;
  // While set, the server has not answered the invocations yet; release answers them.
  holding: boolean;
  release: () => void;
  emit: (methodName: string, payload?: unknown) => void;
  loseConnection: () => void;
  reconnect: () => void;
  close: () => void;
}

// Records hub method calls in the shared log, in order with what the test writes there itself.
function fakeHub(log: string[], failingStarts: number): FakeHub {
  const handlers = new Map<string, (payload: never) => void>();
  const onReconnecting: (() => void)[] = [];
  const onReconnected: (() => void)[] = [];
  const onClose: (() => void)[] = [];
  const unanswered: (() => void)[] = [];
  let failures = failingStarts;

  const hub: FakeHub = {
    starts: 0,
    stopped: false,
    holding: false,
    release: () => {
      unanswered.splice(0).forEach((answer) => {
        answer();
      });
    },
    start: () => {
      hub.starts += 1;

      if (failures > 0) {
        failures -= 1;
        return Promise.reject(new Error("Failed to complete negotiation with the server."));
      }

      return Promise.resolve();
    },
    stop: () => {
      hub.stopped = true;
      return Promise.resolve();
    },
    invoke: (methodName, ...args) => {
      log.push(`${methodName} ${String(args[0])}`);

      return hub.holding
        ? new Promise((resolve) => {
            unanswered.push(() => {
              resolve(undefined);
            });
          })
        : Promise.resolve();
    },
    on: (methodName, handler) => {
      handlers.set(methodName, handler);
    },
    onreconnecting: (callback) => onReconnecting.push(callback),
    onreconnected: (callback) => onReconnected.push(callback),
    onclose: (callback) => onClose.push(callback),
    emit: (methodName, payload) => {
      handlers.get(methodName)?.(payload as never);
    },
    loseConnection: () => {
      onReconnecting.forEach((callback) => {
        callback();
      });
    },
    reconnect: () => {
      onReconnected.forEach((callback) => {
        callback();
      });
    },
    close: () => {
      onClose.forEach((callback) => {
        callback();
      });
    },
  };

  return hub;
}

// A connection over fake hubs. The first failingStarts starts fail.
function connection(failingStarts = 0) {
  const log: string[] = [];
  const hubs: FakeHub[] = [];
  const queryClient = new QueryClient();
  const live = createLiveConnection(queryClient, () => {
    const built = fakeHub(log, failingStarts);
    hubs.push(built);
    return built;
  });

  const hub = () => {
    const current = hubs.at(-1);

    if (current === undefined) {
      throw new Error("No hub was built.");
    }

    return current;
  };

  return { live, log, hub, hubs, queryClient };
}

// Lets every pending promise settle.
function settle(): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, 0);
  });
}

function machine(id: string): MachineSummary {
  return {
    id,
    state: "Pending",
    smbiosUuid: "44454c4c-5700-1038-8036-b7c04f5a344a",
    primaryMac: "00155D010203",
    macAddresses: ["00155D010203"],
    manufacturer: null,
    model: null,
    serialNumber: null,
    assignedName: null,
    agentVersion: null,
    firstSeenUtc: "2026-09-16T10:00:00Z",
    lastSeenUtc: "2026-09-16T10:00:00Z",
    lastSeenAddress: null,
    signedInBy: null,
    firstSeenAddress: null,
    everApproved: false,
    disks: null,
    eligibleDiskCount: null,
    deployment: null,
  };
}

// Everything that may have changed while the connection was down, which every connect reads again.
const resynced = [
  ["machines"],
  ["images"],
  ["packages"],
  ["image-uploads"],
  ["sequences"],
  ["rules"],
  ["machine-sequence"],
  ["sequence"],
];

describe("createLiveConnection", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("refetches the lists on every connect and patches a changed machine into the list", async () => {
    const { live, hub, queryClient } = connection();
    queryClient.setQueryData(["machines"], [machine("m1")]);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    live.start();
    await settle();

    expect(live.status()).toBe("live");
    for (const queryKey of resynced) {
      expect(invalidate).toHaveBeenCalledWith({ queryKey });
    }

    hub().emit("machineChanged", { ...machine("m1"), state: "Approved" });

    expect(queryClient.getQueryData<MachineSummary[]>(["machines"])?.[0]?.state).toBe("Approved");

    invalidate.mockClear();
    hub().loseConnection();
    hub().reconnect();
    await settle();

    for (const queryKey of resynced) {
      expect(invalidate).toHaveBeenCalledWith({ queryKey });
    }
  });

  it("refetches the sequences, the rules and what the rules choose when either changes", async () => {
    const { live, hub, queryClient } = connection();
    live.start();
    await settle();
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    hub().emit("rulesChanged");

    expect(invalidate.mock.calls).toEqual([
      [{ queryKey: ["rules"] }],
      [{ queryKey: ["machine-sequence"] }],
    ]);

    invalidate.mockClear();
    hub().emit("sequenceChanged", { id: "s1", revision: 2, changedBy: "admin" });

    expect(invalidate.mock.calls).toEqual([
      [{ queryKey: ["sequences"] }],
      [{ queryKey: ["rules"] }],
      [{ queryKey: ["machine-sequence"] }],
      [{ queryKey: ["sequence", "s1"] }],
    ]);
  });

  it("reads an open sequence again only when the change is newer than its copy", async () => {
    const { live, hub, queryClient } = connection();
    live.start();
    await settle();
    queryClient.setQueryData(["sequence", "s1"], { id: "s1", revision: 3 });
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const readsOf = () =>
      invalidate.mock.calls.filter(([filters]) => filters?.queryKey?.[0] === "sequence").length;

    hub().emit("sequenceChanged", { id: "s1", revision: 3, changedBy: "admin" });
    expect(readsOf()).toBe(0);

    hub().emit("sequenceChanged", { id: "s1", revision: 4, changedBy: "other" });
    expect(readsOf()).toBe(1);

    hub().emit("sequenceChanged", { id: "s1", revision: null, changedBy: "other" });
    expect(readsOf()).toBe(2);
  });

  it("reads the open sequences again when the library changes, as their problems may have", async () => {
    const { live, hub, queryClient } = connection();
    live.start();
    await settle();
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    hub().emit("imagesChanged");

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["images"] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["sequences"] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["sequence"] });

    invalidate.mockClear();
    hub().emit("packagesChanged");

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["packages"] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["image-uploads"] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["sequence"] });
  });

  it("watches a machine once however many watch it, and unwatches it when the last one stops", async () => {
    const { live, log } = connection();
    live.start();
    await settle();

    const stopFirst = live.watchMachine("m1", {});
    const stopSecond = live.watchMachine("m1", {});

    expect(log).toEqual(["WatchMachine m1"]);

    stopFirst();
    stopFirst();

    expect(log).toEqual(["WatchMachine m1"]);

    stopSecond();

    expect(log).toEqual(["WatchMachine m1", "UnwatchMachine m1"]);

    live.watchMachine("m1", {});

    expect(log).toEqual(["WatchMachine m1", "UnwatchMachine m1", "WatchMachine m1"]);
  });

  it("ignores a repeated unsubscribe once the machine is watched again", async () => {
    const { live, log, hub } = connection();
    live.start();
    await settle();

    const received: unknown[] = [];
    const leave = live.watchMachine("m1", {});
    leave();
    live.watchMachine("m1", { onLogAppended: (event) => received.push(event) });
    leave();

    const lines: MachineLogAppended = { machineId: "m1", lastLineId: 7 };
    hub().emit("machineLogAppended", lines);

    expect(received).toEqual([lines]);
    expect(log).toEqual(["WatchMachine m1", "UnwatchMachine m1", "WatchMachine m1"]);
  });

  it("hands the events of a machine only to its watchers", async () => {
    const { live, hub } = connection();
    live.start();
    await settle();

    const first: unknown[] = [];
    const second: unknown[] = [];
    const stopFirst = live.watchMachine("m1", {
      onLogAppended: (event) => first.push(event),
      onRunStepChanged: (event) => first.push(event),
    });
    live.watchMachine("m2", {
      onLogAppended: (event) => second.push(event),
      onRunStepChanged: (event) => second.push(event),
    });

    const lines: MachineLogAppended = { machineId: "m1", lastLineId: 7 };
    const step: RunStepChanged = {
      machineId: "m2",
      deploymentId: "d1",
      step: {
        stepId: "s1",
        index: 0,
        name: "Partition",
        kind: "partition",
        phase: "WindowsPE",
        state: "Running",
        percent: 0,
        startedUtc: "2026-09-16T10:00:00Z",
        finishedUtc: null,
        error: null,
      },
    };
    hub().emit("machineLogAppended", lines);
    hub().emit("runStepChanged", step);

    expect(first).toEqual([lines]);
    expect(second).toEqual([step]);

    stopFirst();
    hub().emit("machineLogAppended", { machineId: "m1", lastLineId: 8 });

    expect(first).toEqual([lines]);
  });

  it("watches every machine again after a reconnect before telling its watchers", async () => {
    const { live, log, hub } = connection();
    const statuses: string[] = [];
    live.onStatusChange(() => statuses.push(live.status()));
    live.start();
    await settle();

    live.watchMachine("m1", { onReconnect: () => log.push("caught up m1") });
    live.watchMachine("m2", { onReconnect: () => log.push("caught up m2") });
    const stopThird = live.watchMachine("m3", { onReconnect: () => log.push("caught up m3") });
    log.length = 0;

    hub().loseConnection();

    // Groups died with the connection, so leaving one sends nothing.
    stopThird();

    expect(live.status()).toBe("reconnecting");

    hub().holding = true;
    hub().reconnect();
    await settle();

    // Reading what was missed before the machine is watched again could miss more.
    expect(log).toEqual(["WatchMachine m1", "WatchMachine m2"]);

    hub().release();
    await settle();

    expect(log).toEqual(["WatchMachine m1", "WatchMachine m2", "caught up m1", "caught up m2"]);
    expect(statuses).toEqual(["live", "reconnecting", "live"]);
  });

  it("does not tell a watcher that left while its machine was watched again", async () => {
    const { live, log, hub } = connection();
    live.start();
    await settle();

    const leave = live.watchMachine("m1", { onReconnect: () => log.push("caught up m1") });
    live.watchMachine("m2", { onReconnect: () => log.push("caught up m2") });
    log.length = 0;

    hub().loseConnection();
    hub().holding = true;
    hub().reconnect();
    await settle();
    leave();
    hub().release();
    await settle();

    expect(log).toEqual([
      "WatchMachine m1",
      "WatchMachine m2",
      "UnwatchMachine m1",
      "caught up m2",
    ]);
  });

  it("tells no watcher once stopped while the machines were watched again", async () => {
    const { live, log, hub } = connection();
    live.start();
    await settle();

    live.watchMachine("m1", { onReconnect: () => log.push("caught up m1") });
    log.length = 0;

    hub().loseConnection();
    hub().holding = true;
    hub().reconnect();
    await settle();
    live.stop();
    hub().release();
    await settle();

    expect(hub().stopped).toBe(true);
    expect(log).toEqual(["WatchMachine m1"]);
  });

  it("watches the machines watched before the first connect once it is up", async () => {
    const { live, log } = connection();

    live.watchMachine("m1", { onReconnect: () => log.push("caught up m1") });

    expect(live.status()).toBe("offline");
    expect(log).toEqual([]);

    live.start();
    await settle();

    expect(log).toEqual(["WatchMachine m1", "caught up m1"]);
  });

  it("retries a failed first start, waiting longer each time", async () => {
    vi.useFakeTimers();
    const { live, hub } = connection(2);

    live.start();

    expect(hub().starts).toBe(1);

    await vi.advanceTimersByTimeAsync(999);

    expect(hub().starts).toBe(1);
    expect(live.status()).toBe("offline");

    await vi.advanceTimersByTimeAsync(1);

    expect(hub().starts).toBe(2);

    await vi.advanceTimersByTimeAsync(1_999);

    expect(hub().starts).toBe(2);

    await vi.advanceTimersByTimeAsync(1);

    expect(hub().starts).toBe(3);
    await vi.waitFor(() => {
      expect(live.status()).toBe("live");
    });
  });

  it("does not retry a failed start once stopped", async () => {
    vi.useFakeTimers();
    const { live, hub, hubs } = connection(1);

    live.start();
    await vi.advanceTimersByTimeAsync(0);
    live.stop();
    await vi.advanceTimersByTimeAsync(5_000);

    expect(hubs).toHaveLength(1);
    expect(hub().starts).toBe(1);
    expect(live.status()).toBe("offline");
  });

  it("starts again after the connection closed, but not once stopped", async () => {
    const { live, hub, hubs } = connection();
    live.start();
    await settle();

    hub().close();
    await settle();

    expect(hub().starts).toBe(2);
    expect(live.status()).toBe("live");

    live.stop();
    hub().close();
    await settle();

    expect(hub().stopped).toBe(true);
    expect(hub().starts).toBe(2);
    expect(live.status()).toBe("offline");
    expect(hubs).toHaveLength(1);
  });

  it("keeps its watchers when stopped and started again, as strict mode does", async () => {
    const { live, log, hubs } = connection();
    live.watchMachine("m1", {});

    live.start();
    live.stop();
    live.start();
    await settle();

    expect(hubs).toHaveLength(2);
    expect(hubs[0]?.stopped).toBe(true);
    expect(log).toEqual(["WatchMachine m1"]);
    expect(live.status()).toBe("live");
  });
});

describe("useMachineWatch", () => {
  it("is offline without a connection", () => {
    const { result } = renderHook(() => useMachineWatch("m1", {}));

    expect(result.current).toBe("offline");
  });

  it("watches the machine while mounted, hands over its events and follows the status", async () => {
    const { live, log, hub } = connection();
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(LiveContext, { value: live }, children);
    const received: unknown[] = [];

    const { result, unmount } = renderHook(
      () =>
        useMachineWatch("m1", {
          onLogAppended: (event) => received.push(event),
        }),
      { wrapper },
    );

    expect(result.current).toBe("offline");

    await act(async () => {
      live.start();
      await settle();
    });

    expect(result.current).toBe("live");
    expect(log).toEqual(["WatchMachine m1"]);

    act(() => {
      hub().emit("machineLogAppended", { machineId: "m1", lastLineId: 3 });
    });

    expect(received).toEqual([{ machineId: "m1", lastLineId: 3 }]);

    unmount();

    expect(log).toEqual(["WatchMachine m1", "UnwatchMachine m1"]);
  });
});
