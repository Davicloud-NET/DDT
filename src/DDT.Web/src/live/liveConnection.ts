// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";

import { backoff } from "@/lib/backoff";
import { createListeners } from "@/lib/listeners";

import { cacheEventHandlers, reconnectKeys } from "./cacheEvents";
import {
  createWatchRegistry,
  watchAgain,
  watchEventHandlers,
  watchThrough,
  type MachineWatchHandlers,
} from "./machineWatches";

// A hub event's handler. The never lets each handler declare the payload of its own event.
export type EventHandler = (payload: never) => void;

// The part of SignalR's HubConnection the live connection uses, so tests can hand in a fake hub.
export interface LiveHub {
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>;
  on(methodName: string, handler: EventHandler): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
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

function buildHub(): LiveHub {
  return new HubConnectionBuilder()
    .withUrl("/hubs/live")
    .withAutomaticReconnect({
      // SignalR counts the retries before this one; backoff counts failures, the first as 1.
      nextRetryDelayInMilliseconds: (retry) => backoff(retry.previousRetryCount + 1),
    })
    .configureLogging(LogLevel.Warning)
    .build();
}

// A refused watch, for example past the server's limit per connection, only means that no events come for
// that machine; its page still reads what it shows.
function invokeQuietly(hub: LiveHub, methodName: string, machineId: string): void {
  hub.invoke(methodName, machineId).catch(() => undefined);
}

// The signed in application's one hub connection. Every connect reads again what events sent while it was down would
// have patched. start and stop may alternate, as in React's strict mode; each start builds a new hub.
export function createLiveConnection(
  queryClient: QueryClient,
  build: () => LiveHub = buildHub,
): LiveConnection {
  const watches = createWatchRegistry();
  const handlers = { ...cacheEventHandlers(queryClient), ...watchEventHandlers(watches) };
  const statusListeners = createListeners();

  let hub: LiveHub | null = null;
  let status: LiveStatus = "offline";
  let retryTimer: ReturnType<typeof setTimeout> | undefined;

  const setStatus = (next: LiveStatus) => {
    if (status !== next) {
      status = next;
      statusListeners.notify();
    }
  };

  const connected = async (current: LiveHub) => {
    setStatus("live");

    for (const queryKey of reconnectKeys()) {
      void queryClient.invalidateQueries({ queryKey });
    }

    await watchAgain(watches, current, () => hub === current);
  };

  // Automatic reconnect covers a dropped connection but not a failed first start, so that is retried here.
  const connect = async (current: LiveHub, attempt: number): Promise<void> => {
    try {
      await current.start();
    } catch {
      if (hub === current) {
        retryTimer = setTimeout(() => void connect(current, attempt + 1), backoff(attempt + 1));
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

    for (const [event, handler] of Object.entries(handlers)) {
      current.on(event, handler);
    }

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

  return {
    start,
    stop,
    status: () => status,
    onStatusChange: statusListeners.subscribe,
    // While the connection is down there are no groups to join or leave; connected watches every machine again.
    watchMachine: watchThrough(watches, (methodName, machineId) => {
      if (hub !== null && status === "live") {
        invokeQuietly(hub, methodName, machineId);
      }
    }),
  };
}
