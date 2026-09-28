// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { DeploymentStepView } from "@/deployments/deployments";

import type { EventHandler, LiveHub } from "./liveConnection";

// The server's MachineLogAppendedEvent: the machine has log lines up to this id.
export interface MachineLogAppended {
  machineId: string;
  lastLineId: number;
}

// The server's RunStepChangedEvent: a step of the machine's run changed. A node of a tree carries its place in it
// and its latest visit: pass, iteration, branch and evaluation.
export interface RunStepChanged {
  machineId: string;
  deploymentId: string;
  step: DeploymentStepView;
}

// The server's runVariablesChanged: the agent reported the sequence's variables anew, all of them.
export interface RunVariablesChanged {
  machineId: string;
  deploymentId: string;
  variables: Record<string, string>;
}

// The server sends these events only to the connections that watch the machine.
export interface MachineWatchHandlers {
  onLogAppended?: (event: MachineLogAppended) => void;
  onRunStepChanged?: (event: RunStepChanged) => void;
  onRunVariablesChanged?: (event: RunVariablesChanged) => void;
  // Called once the machine is watched again after the connection was lost, or first came up after the
  // watch began. Events sent meanwhile are lost, so this is when a watcher reads what it missed.
  onReconnect?: () => void;
}

// One watcher; the registry tells watchers apart by identity, so one page may watch a machine twice.
export interface MachineWatch {
  handlers: MachineWatchHandlers;
}

export interface WatchRegistry {
  // True for a machine's first watcher, whose machine the hub has to watch.
  add: (machineId: string, watch: MachineWatch) => boolean;
  // True once the machine's last watcher left, so the hub can stop watching it. A repeated remove is false.
  remove: (machineId: string, watch: MachineWatch) => boolean;
  has: (machineId: string, watch: MachineWatch) => boolean;
  machineIds: () => string[];
  watchesOf: (machineId: string) => MachineWatch[];
  // Every watcher with its machine, as the registry holds them now.
  entries: () => { machineId: string; watch: MachineWatch }[];
}

// Watchers of one machine share its group on the hub, so only the first and the last need a call.
export function createWatchRegistry(): WatchRegistry {
  const watchers = new Map<string, Set<MachineWatch>>();

  return {
    add: (machineId, watch) => {
      const watches = watchers.get(machineId);

      if (watches !== undefined) {
        watches.add(watch);
        return false;
      }

      watchers.set(machineId, new Set([watch]));
      return true;
    },
    remove: (machineId, watch) => {
      const watches = watchers.get(machineId);

      if (watches === undefined || !watches.delete(watch) || watches.size > 0) {
        return false;
      }

      watchers.delete(machineId);
      return true;
    },
    has: (machineId, watch) => watchers.get(machineId)?.has(watch) === true,
    machineIds: () => [...watchers.keys()],
    watchesOf: (machineId) => [...(watchers.get(machineId) ?? [])],
    entries: () =>
      [...watchers].flatMap(([machineId, watches]) =>
        [...watches].map((watch) => ({ machineId, watch })),
      ),
  };
}

// Watches a machine, and asks the hub through invoke to watch or unwatch it where the registry says so. Returns the
// unwatch.
export function watchThrough(
  registry: WatchRegistry,
  invoke: (methodName: string, machineId: string) => void,
) {
  return (machineId: string, handlers: MachineWatchHandlers): (() => void) => {
    const watch = { handlers };

    if (registry.add(machineId, watch)) {
      invoke("WatchMachine", machineId);
    }

    return () => {
      if (registry.remove(machineId, watch)) {
        invoke("UnwatchMachine", machineId);
      }
    };
  };
}

// Groups do not survive a lost connection, so every watched machine is watched again before its watchers read
// what they missed. Nobody is told once the hub is no longer the current one.
export async function watchAgain(
  registry: WatchRegistry,
  hub: LiveHub,
  isCurrent: () => boolean,
): Promise<void> {
  const missed = registry.entries();

  await Promise.allSettled(
    registry.machineIds().map((machineId) => hub.invoke("WatchMachine", machineId)),
  );

  for (const { machineId, watch } of missed) {
    if (isCurrent() && registry.has(machineId, watch)) {
      watch.handlers.onReconnect?.();
    }
  }
}

// The hub's events for watched machines, each handed to the machine's watchers.
export function watchEventHandlers(registry: WatchRegistry) {
  return {
    machineLogAppended: (event: MachineLogAppended) => {
      for (const watch of registry.watchesOf(event.machineId)) {
        watch.handlers.onLogAppended?.(event);
      }
    },
    runStepChanged: (event: RunStepChanged) => {
      for (const watch of registry.watchesOf(event.machineId)) {
        watch.handlers.onRunStepChanged?.(event);
      }
    },
    runVariablesChanged: (event: RunVariablesChanged) => {
      for (const watch of registry.watchesOf(event.machineId)) {
        watch.handlers.onRunVariablesChanged?.(event);
      }
    },
  } satisfies Record<string, EventHandler>;
}
