// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";

import type { DeploymentStepView } from "@/deployments/deployments";
import { imagesQuery, uploadsQuery } from "@/images/images";
import {
  machinesQuery,
  removeMachines,
  upsertMachine,
  type MachinesRemoved,
  type MachineSummary,
} from "@/machines/machines";
import { packagesQuery } from "@/packages/packages";
import { rulesQuery, sequenceResolutionsKey } from "@/rules/rules";
import {
  sequenceDocumentsKey,
  sequenceQuery,
  sequencesQuery,
  type SequenceChanged,
} from "@/sequences/sequences";

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

  const refetchRules = () => {
    void queryClient.invalidateQueries({ queryKey: rulesQuery.queryKey });
    refetchResolutions();
  };

  // A rule shows the name of the sequence it chooses.
  const refetchSequences = () => {
    void queryClient.invalidateQueries({ queryKey: sequencesQuery.queryKey });
    refetchRules();
  };

  // A sequence's problems depend on the library, so a change there reads the open sequences again. An editor
  // takes the new problems and keeps its unsaved edits.
  const refetchLibrary = () => {
    refetchSequences();
    void queryClient.invalidateQueries({ queryKey: sequenceDocumentsKey });
  };

  // A copy at least as new as the change, such as the one this page's own save returned, is kept.
  const sequenceChanged = (event: SequenceChanged) => {
    refetchSequences();

    const cached = queryClient.getQueryData(sequenceQuery(event.id).queryKey);

    if (cached === undefined || event.revision === null || cached.revision < event.revision) {
      void queryClient.invalidateQueries({ queryKey: sequenceQuery(event.id).queryKey });
    }
  };

  // An upload another administrator finished no longer waits to be resumed.
  const packagesChanged = () => {
    void queryClient.invalidateQueries({ queryKey: packagesQuery.queryKey });
    void queryClient.invalidateQueries({ queryKey: uploadsQuery.queryKey });
    refetchLibrary();
  };

  const watchesOf = (machineId: string) => [...(watchers.get(machineId) ?? [])];

  // Groups do not survive a lost connection, so every watched machine is watched again before its
  // watchers read what they missed.
  const connected = async (current: LiveHub) => {
    setStatus("live");
    refetchMachines();
    refetchImages();
    packagesChanged();

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

    current.on("machinesRemoved", (event: MachinesRemoved) => {
      removeMachines(queryClient, event.machineIds);
    });

    current.on("imagesChanged", () => {
      refetchImages();
      refetchLibrary();
    });

    current.on("packagesChanged", packagesChanged);

    current.on("sequenceChanged", sequenceChanged);

    current.on("rulesChanged", refetchRules);

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
