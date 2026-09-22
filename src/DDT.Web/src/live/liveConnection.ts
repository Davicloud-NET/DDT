// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";

import type { DeploymentStepView } from "@/deployments/deployments";
import { imagesQuery } from "@/images/images";
import { machinesQuery, upsertMachine, type MachineSummary } from "@/machines/machines";
import { sequenceResolutionsKey } from "@/rules/rules";
import { sequencesQuery } from "@/sequences/sequences";

// The part of SignalR's HubConnection the live connection uses, so tests can hand in a fake hub. The never
// lets each handler declare the payload of its own event.
export interface LiveHub {
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>;
  on(methodName: string, handler: (payload: never) => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
}

// The server's MachineLogAppendedEvent: the machine has log lines up to this id.
export interface MachineLogAppended {
  machineId: string;
  lastLineId: number;
}

// The server's RunStepChangedEvent: a step of the machine's run changed.
export interface RunStepChanged {
  machineId: string;
  deploymentId: string;
  step: DeploymentStepView;
}

// The server sends these events only to the connections that watch the machine.
export interface MachineWatchHandlers {
  onLogAppended?: (event: MachineLogAppended) => void;
  onRunStepChanged?: (event: RunStepChanged) => void;
  // Called once the machine is watched again after the connection was lost, or first came up after the
  // watch began. Events sent meanwhile are lost, so this is when a watcher reads what it missed.
  onReconnect?: () => void;
}

// "reconnecting" while SignalR brings a dropped connection back; "offline" when there is none, including
// before the first connect and while a failed start waits to be retried.
export type LiveStatus = "live" | "reconnecting" | "offline";

export interface LiveConnection {
  start: () => void;
  stop: () => void;
  status: () => LiveStatus;
  // Returns the unsubscribe.
  onStatusChange: (listener: () => void) => () => void;
  // Returns the unsubscribe. Watchers of one machine share its group on the hub.
  watchMachine: (machineId: string, handlers: MachineWatchHandlers) => () => void;
}

function backoff(attempt: number): number {
  return Math.min(30_000, 1_000 * 2 ** attempt);
}

function buildHub(): LiveHub {
  return new HubConnectionBuilder()
    .withUrl("/hubs/live")
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (retry) => backoff(retry.previousRetryCount),
    })
    .configureLogging(LogLevel.Warning)
    .build();
}

// A refused watch, for example past the server's limit per connection, only means that no events come for
// that machine; its page still reads what it shows.
function invokeQuietly(hub: LiveHub, methodName: string, machineId: string): void {
  hub.invoke(methodName, machineId).catch(() => undefined);
}

// One connection for the signed in application. Events patch the query cache directly. Anything sent
// while disconnected is lost, so every successful connect refetches what the events would have patched.
// start and stop may alternate, as React's strict mode does; each start builds a new hub.
export function createLiveConnection(
  queryClient: QueryClient,
  build: () => LiveHub = buildHub,
): LiveConnection {
  const watchers = new Map<string, Set<{ handlers: MachineWatchHandlers }>>();
  const statusListeners = new Set<() => void>();

  let hub: LiveHub | null = null;
  let status: LiveStatus = "offline";
  let retryTimer: ReturnType<typeof setTimeout> | undefined;

  const setStatus = (next: LiveStatus) => {
    if (status !== next) {
      status = next;

      for (const listener of [...statusListeners]) {
        listener();
      }
    }
  };

  const refetchMachines = () => {
    void queryClient.invalidateQueries({ queryKey: machinesQuery.queryKey });
  };

  const refetchImages = () => {
    void queryClient.invalidateQueries({ queryKey: imagesQuery.queryKey });
  };

  // What a machine would run depends on the rules and on whether the chosen sequence has problems.
  const refetchResolutions = () => {
    void queryClient.invalidateQueries({ queryKey: sequenceResolutionsKey });
  };

  const refetchSequences = () => {
    void queryClient.invalidateQueries({ queryKey: sequencesQuery.queryKey });
    refetchResolutions();
  };

  const watchesOf = (machineId: string) => [...(watchers.get(machineId) ?? [])];

  // Groups do not survive a lost connection, so every watched machine is watched again before its
  // watchers read what they missed.
  const connected = async (current: LiveHub) => {
    setStatus("live");
    refetchMachines();
    refetchImages();
    refetchSequences();

    const missed = [...watchers].flatMap(([machineId, watches]) =>
      [...watches].map((watch) => ({ machineId, watch })),
    );

    await Promise.allSettled(
      [...watchers.keys()].map((machineId) => current.invoke("WatchMachine", machineId)),
    );

    for (const { machineId, watch } of missed) {
      if (hub === current && watchers.get(machineId)?.has(watch) === true) {
        watch.handlers.onReconnect?.();
      }
    }
  };

  // Automatic reconnect covers a dropped connection but not a failed first start, so that is retried here.
  const connect = async (current: LiveHub, attempt: number): Promise<void> => {
    try {
      await current.start();
    } catch {
      if (hub === current) {
        retryTimer = setTimeout(() => void connect(current, attempt + 1), backoff(attempt));
      }

      return;
    }

    if (hub === current) {
      await connected(current);
    }
  };

  const start = () => {
    if (hub !== null) {
      return;
    }

    const current = build();
    hub = current;

    current.on("machineChanged", (machine: MachineSummary) => {
      upsertMachine(queryClient, machine);
    });

    current.on("machinesRemoved", refetchMachines);

    current.on("imagesChanged", refetchImages);

    current.on("sequenceChanged", refetchSequences);

    current.on("rulesChanged", refetchResolutions);

    current.on("machineLogAppended", (event: MachineLogAppended) => {
      for (const watch of watchesOf(event.machineId)) {
        watch.handlers.onLogAppended?.(event);
      }
    });

    current.on("runStepChanged", (event: RunStepChanged) => {
      for (const watch of watchesOf(event.machineId)) {
        watch.handlers.onRunStepChanged?.(event);
      }
    });

    current.onreconnecting(() => {
      if (hub === current) {
        setStatus("reconnecting");
      }
    });

    current.onreconnected(() => {
      if (hub === current) {
        void connected(current);
      }
    });

    current.onclose(() => {
      if (hub === current) {
        setStatus("offline");
        void connect(current, 0);
      }
    });

    void connect(current, 0);
  };

  const stop = () => {
    const current = hub;

    if (current === null) {
      return;
    }

    hub = null;
    clearTimeout(retryTimer);
    setStatus("offline");
    void current.stop();
  };

  const watchMachine = (machineId: string, handlers: MachineWatchHandlers) => {
    const watch = { handlers };
    let watches = watchers.get(machineId);

    if (watches === undefined) {
      watches = new Set();
      watchers.set(machineId, watches);

      if (hub !== null && status === "live") {
        invokeQuietly(hub, "WatchMachine", machineId);
      }
    }

    watches.add(watch);
    const own = watches;

    return () => {
      if (!own.delete(watch) || own.size > 0) {
        return;
      }

      watchers.delete(machineId);

      if (hub !== null && status === "live") {
        invokeQuietly(hub, "UnwatchMachine", machineId);
      }
    };
  };

  return {
    start,
    stop,
    status: () => status,
    onStatusChange: (listener) => {
      statusListeners.add(listener);

      return () => {
        statusListeners.delete(listener);
      };
    },
    watchMachine,
  };
}
