// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";

// A request the page sent. body is the parsed JSON when the page sent JSON, else null; raw is what it sent.
export interface Sent {
  method: string;
  path: string;
  body: unknown;
  raw: BodyInit | null | undefined;
  headers: Headers;
  signal: AbortSignal | null | undefined;
}

// A fixed answer, or a function of the request for a server that changes as the test goes.
export type Answer =
  | { status?: number; body?: unknown; headers?: Record<string, string> }
  | ((request: Sent) => Response | Promise<Response>);

export type Routes = Record<string, Answer>;

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(status === 204 ? null : JSON.stringify(body), { status, headers });
}

export interface TestServer {
  // Every request in order, the session and the current user included.
  requests: Sent[];
  // The answers by "METHOD path", which a test may change while the page is open.
  routes: Routes;
  // How often the page sent "METHOD path".
  count: (call: string) => number;
  // The requests that changed something: every method but GET.
  changes: () => Sent[];
}

// Stands in for the server through fetch, with the CSRF token and the current user built in (401 for a null user) and
// 404 for anything else unanswered. An aborted request fails at once, as with a browser's fetch.
export function serve(user: CurrentUser | null, routes: Routes = {}): TestServer {
  const requests: Sent[] = [];

  const answer = async (request: Sent): Promise<Response> => {
    if (request.path === "/api/auth/session") {
      return new Response(null, { headers: { "X-CSRF-TOKEN": "token" } });
    }

    const route = routes[`${request.method} ${request.path}`];

    if (route !== undefined) {
      return typeof route === "function"
        ? route(request)
        : json(route.body ?? null, route.status ?? 200, route.headers ?? {});
    }

    if (request.method === "GET" && request.path === "/api/auth/me") {
      return user === null ? new Response(null, { status: 401 }) : json(user);
    }

    return new Response(null, { status: 404 });
  };

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : input.toString();
      const request: Sent = {
        method: init?.method ?? "GET",
        path: url.replace("http://localhost", ""),
        body: typeof init?.body === "string" ? (JSON.parse(init.body) as unknown) : null,
        raw: init?.body,
        headers: new Headers(init?.headers),
        signal: init?.signal,
      };

      requests.push(request);

      const signal = init?.signal;

      return new Promise<Response>((resolve, reject) => {
        const abort = () => {
          reject(new DOMException("The operation was aborted.", "AbortError"));
        };

        if (signal?.aborted === true) {
          abort();
          return;
        }

        signal?.addEventListener("abort", abort, { once: true });
        answer(request).then(resolve, reject);
      });
    }),
  );

  return {
    requests,
    routes,
    count: (call) =>
      requests.filter((request) => `${request.method} ${request.path}` === call).length,
    changes: () => requests.filter((request) => request.method !== "GET"),
  };
}
