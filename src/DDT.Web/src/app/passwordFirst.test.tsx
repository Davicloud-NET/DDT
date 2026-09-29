// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { LiveConnection } from "@/live/liveConnection";
import { administrator, json, stubServer } from "@/test/serve";

import { createAppRouter } from "./router";

// Records whether the shell asks for its live connection on or off.
const live = vi.hoisted(() => ({ enabled: [] as boolean[] }));

vi.mock("@/live/useLiveUpdates", () => ({
  useLiveUpdates: (enabled = true): LiveConnection => {
    live.enabled.push(enabled);

    return {
      start: () => undefined,
      stop: () => undefined,
      status: () => "live",
      onStatusChange: () => () => undefined,
      watchMachine: () => () => undefined,
    };
  },
}));

function start(at: string) {
  window.history.pushState({}, "", at);

  const requests = stubServer(
    { ...administrator, mustChangePassword: true },
    {
      "POST /api/auth/password": () =>
        new Response(null, { status: 200, headers: { "Content-Length": "0" } }),
      "GET /api/tokens": () => json([]),
      "GET /api/users": () => json([]),
      "GET /api/directory": () =>
        json({ enabled: false, host: null, baseDn: null, groupRoleMap: [] }),
    },
  );
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createAppRouter(queryClient);

  render(
    <I18nProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </I18nProvider>,
  );

  return { requests, router };
}

describe("an account that has to set its own password", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    live.enabled.length = 0;
    window.history.pushState({}, "", "/");
  });

  it("is kept on the Account page, with no navigation and no live connection, until it has set one", async () => {
    const { requests, router } = start("/machines");

    expect(await screen.findByText("Set a password of your own first")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/account");
    expect(
      within(screen.getByRole("navigation", { name: "Sections" })).queryAllByRole("link"),
    ).toEqual([]);
    expect(live.enabled.every((enabled) => !enabled)).toBe(true);

    await act(() => router.navigate({ to: "/admin/users" }));
    expect(router.state.location.pathname).toBe("/account");
    expect(requests.some((request) => request.path === "/api/users")).toBe(false);

    fireEvent.change(screen.getByLabelText("Current password"), {
      target: { value: "Tq8v-Rk3m-Wz6p-Hd2n" },
    });
    fireEvent.change(screen.getByLabelText("New password"), {
      target: { value: "a long one of my own" },
    });
    fireEvent.change(screen.getByLabelText("New password again"), {
      target: { value: "a long one of my own" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Change password" }));

    expect(
      await screen.findByText("Password changed. The rest of DDT is open to you now."),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(
        within(screen.getByRole("navigation", { name: "Sections" })).getByRole("link", {
          name: "Administration",
        }),
      ).toBeInTheDocument();
    });
    expect(live.enabled.at(-1)).toBe(true);

    await act(() => router.navigate({ to: "/admin/users" }));
    expect(await screen.findByRole("heading", { name: "Users and roles" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/admin/users");
  });
});
