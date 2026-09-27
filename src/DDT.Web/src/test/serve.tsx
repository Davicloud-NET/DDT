// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
  type RouteComponent,
} from "@tanstack/react-router";
import { render } from "@testing-library/react";
import { vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";

// A page on a fake server, for page tests: fetch answers "METHOD path" from the handlers, plus the signed-in user and
// the CSRF token, and every request is recorded, so a test can tell that a list was not read again.

export interface Sent {
  method: string;
  path: string;
  body: unknown;
  // Lower-case names, as Headers keeps them.
  headers: Record<string, string>;
}

export type Handler = (request: Sent) => Response;

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

export function problem(
  title: string,
  status: number,
  errors?: Record<string, string[]>,
): Response {
  return json({ title, status, ...(errors === undefined ? {} : { errors }) }, status);
}

export function noContent(): Response {
  return new Response(null, { status: 204 });
}

export const administrator: CurrentUser = {
  id: "0193a4b2-0000-7000-8000-00000000a001",
  userName: "admin",
  displayName: "Ada Admin",
  source: "Local",
  twoFactorEnabled: false,
  mustChangePassword: false,
  roles: ["Administrator"],
};

export const operator: CurrentUser = {
  ...administrator,
  id: "0193a4b2-0000-7000-8000-00000000a002",
  userName: "operator",
  displayName: null,
  roles: ["Operator"],
};

// Answers fetch from the handlers. Anything no handler answers is a 404.
export function stubServer(user: CurrentUser | null, handlers: Record<string, Handler>): Sent[] {
  const requests: Sent[] = [];

  vi.stubGlobal("scrollTo", vi.fn());
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : input.toString();
      const request: Sent = {
        method: init?.method ?? "GET",
        path: url.replace("http://localhost", ""),
        body: typeof init?.body === "string" ? JSON.parse(init.body) : null,
        headers: Object.fromEntries(new Headers(init?.headers).entries()),
      };

      requests.push(request);

      if (request.path === "/api/auth/me") {
        return Promise.resolve(user === null ? new Response(null, { status: 401 }) : json(user));
      }

      if (request.path === "/api/auth/session") {
        return Promise.resolve(new Response(null, { headers: { "X-CSRF-TOKEN": "token" } }));
      }

      const handler = handlers[`${request.method} ${request.path}`];

      return Promise.resolve(
        handler === undefined ? new Response(null, { status: 404 }) : handler(request),
      );
    }),
  );

  return requests;
}

// The clipboard a copy key writes to; jsdom has none.
export function stubClipboard(): { written: string[] } {
  const written: string[] = [];

  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: {
      writeText: vi.fn((text: string) => {
        written.push(text);
        return Promise.resolve();
      }),
    },
  });

  return { written };
}

// Renders one page at its path inside a shell route, as the application does, on the fake server.
export function servePage({
  user,
  handlers,
  path,
  component,
  search = "",
}: {
  user: CurrentUser | null;
  handlers: Record<string, Handler>;
  path: string;
  component: RouteComponent;
  search?: string;
}) {
  const requests = stubServer(user, handlers);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  if (user !== null) {
    queryClient.setQueryData(["current-user"], user);
  }

  const rootRoute = createRootRoute();
  const shellRoute = createRoute({ getParentRoute: () => rootRoute, id: "shell" });
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      shellRoute.addChildren([createRoute({ getParentRoute: () => shellRoute, path, component })]),
    ]),
    history: createMemoryHistory({ initialEntries: [`${path}${search}`] }),
  });

  render(
    <I18nProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </I18nProvider>,
  );

  return { requests, queryClient, router };
}

// How often a path was read, to tell that an action patched a list rather than reading it again.
export function reads(requests: readonly Sent[], path: string): number {
  return requests.filter((request) => request.method === "GET" && request.path === path).length;
}
