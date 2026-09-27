// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { QueryClient } from "@tanstack/react-query";

import { createLiveConnection, type LiveConnection, type LiveHub } from "@/live/liveConnection";

// The application's live connection over a hub the test drives: it pushes the server's events, and loses and
// regains the connection. The events go through the same handlers as in the application, into the query cache.
export interface TestHub {
  live: LiveConnection;
  // The hub methods the page invoked, such as "WatchMachine <id>", in order.
  invocations: string[];
  push: (event: string, payload?: unknown) => void;
  loseConnection: () => void;
  reconnect: () => void;
}

export function testHub(queryClient: QueryClient): TestHub {
  const handlers = new Map<string, (payload: never) => void>();
  const onReconnecting: (() => void)[] = [];
  const onReconnected: (() => void)[] = [];
  const invocations: string[] = [];

  const hub: LiveHub = {
    start: () => Promise.resolve(),
    stop: () => Promise.resolve(),
    invoke: (methodName, ...args) => {
      invocations.push(`${methodName} ${String(args[0])}`);
      return Promise.resolve();
    },
    on: (methodName, handler) => {
      handlers.set(methodName, handler);
    },
    onreconnecting: (callback) => onReconnecting.push(callback),
    onreconnected: (callback) => onReconnected.push(callback),
    onclose: () => undefined,
  };

  return {
    live: createLiveConnection(queryClient, () => hub),
    invocations,
    push: (event, payload) => {
      const handler = handlers.get(event);

      if (handler === undefined) {
        throw new Error(`The live connection does not handle ${event}.`);
      }

      handler(payload as never);
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
  };
}
