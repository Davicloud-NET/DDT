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

// The application's own router, which reads the browser's address. A null user has not signed in. Every
// sign-in is refused, and its body is kept in logins.
function open(path: string, user: CurrentUser | null, logins: unknown[] = []) {
  vi.stubGlobal("scrollTo", vi.fn());
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : input.toString();

      switch (url.replace("http://localhost", "")) {
        case "/api/auth/me":
          return Promise.resolve(
            user === null
              ? new Response(null, { status: 401 })
              : new Response(JSON.stringify(user), { status: 200 }),
          );
        case "/api/auth/login":
          logins.push(JSON.parse(init?.body as string));
          return Promise.resolve(new Response(null, { status: 401 }));
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

describe("the sign-in page", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.history.replaceState(null, "", "/");
  });

  it("asks only for the code of an account the server sent back from OpenID Connect", async () => {
    const logins: unknown[] = [];
    open("/sign-in?step=two-factor", null, logins);

    fireEvent.change(await screen.findByLabelText("Authentication code"), {
      target: { value: "123456" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("That code is not valid.");
    expect(screen.queryByLabelText("User name")).not.toBeInTheDocument();
    expect(logins).toEqual([{ userName: "", password: "", twoFactorCode: "123456" }]);
  });

  it("asks for the user name and password otherwise", async () => {
    open("/sign-in?step=something-else&error=something-else", null);

    expect(await screen.findByLabelText("User name")).toBeInTheDocument();
    expect(screen.queryByLabelText("Authentication code")).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it.each([
    ["locked", "This account is locked. Try again later or ask an administrator."],
    ["not-allowed", "This account may not sign in. Ask an administrator."],
  ])("says why the server refused an OpenID Connect sign-in: %s", async (error, message) => {
    open(`/sign-in?error=${error}`, null);

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    expect(screen.getByLabelText("User name")).toBeInTheDocument();
  });
});

describe("the navigation", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.history.replaceState(null, "", "/");
  });

  it("marks Machines on a machine's page", async () => {
    open("/machines/0193a4b2-0000-7000-8000-000000000009", administrator);

    expect(
      await screen.findByText(
        "This machine was removed. It registers as a new machine at its next netboot.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Machines" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Images" })).not.toHaveAttribute("aria-current");
  });

  it("marks Sequences in a sequence's editor", async () => {
    open("/sequences/0193a4b2-0000-7000-8000-0000000000e1", administrator);

    expect(await screen.findByRole("link", { name: "All sequences" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Sequences" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Machines" })).not.toHaveAttribute("aria-current");
  });
});
