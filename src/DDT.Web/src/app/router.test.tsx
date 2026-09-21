// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";

import { createAppRouter } from "./router";

vi.mock("@/live/useLiveUpdates", () => ({ useLiveUpdates: vi.fn() }));

const administrator: CurrentUser = {
  id: "0193a4b2-0000-7000-8000-000000000001",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  roles: ["Administrator"],
};

// The application's own router, which reads the browser's address. A null user has not signed in.
function open(path: string, user: CurrentUser | null) {
  vi.stubGlobal("scrollTo", vi.fn());
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL) => {
      const url = input instanceof Request ? input.url : input.toString();

      switch (url.replace("http://localhost", "")) {
        case "/api/auth/me":
          return Promise.resolve(
            user === null
              ? new Response(null, { status: 401 })
              : new Response(JSON.stringify(user), { status: 200 }),
          );
        case "/api/machines":
          return Promise.resolve(new Response("[]", { status: 200 }));
        default:
          return Promise.resolve(new Response(null, { status: 404 }));
      }
    }),
  );

  window.history.replaceState(null, "", path);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={createAppRouter(queryClient)} />
    </QueryClientProvider>,
  );
}

describe("the About page", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.history.replaceState(null, "", "/");
  });

  it("opens without signing in", async () => {
    open("/about", null);

    expect(await screen.findByRole("heading", { name: "About DDT" })).toBeInTheDocument();
    expect(window.location.pathname).toBe("/about");
  });

  it("is linked from the sign-in page", async () => {
    open("/sign-in", null);

    fireEvent.click(await screen.findByRole("link", { name: "About DDT" }));

    expect(await screen.findByRole("heading", { name: "About DDT" })).toBeInTheDocument();
    expect(window.location.pathname).toBe("/about");
  });

  it("is linked from the navigation once signed in", async () => {
    open("/", administrator);

    fireEvent.click(await screen.findByRole("link", { name: "About DDT" }));

    expect(await screen.findByRole("heading", { name: "About DDT" })).toBeInTheDocument();
    expect(window.location.pathname).toBe("/about");
  });
});
