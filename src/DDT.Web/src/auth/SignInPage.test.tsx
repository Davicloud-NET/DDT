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
} from "@tanstack/react-router";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { json, stubServer, type Handler } from "@/test/serve";

import { SignInPage } from "./SignInPage";

function serve(handlers: Record<string, Handler>, at = "/sign-in") {
  const requests = stubServer(null, {
    "GET /api/auth/external/providers": () => json([]),
    ...handlers,
  });
  const rootRoute = createRootRoute();
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/sign-in",
        validateSearch: (search: Record<string, unknown>): { error?: string } =>
          typeof search.error === "string" ? { error: search.error } : {},
        component: SignInPage,
      }),
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/machines",
        component: () => <p>The machines</p>,
      }),
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/about",
        component: () => <p>About</p>,
      }),
    ]),
    history: createMemoryHistory({ initialEntries: [at] }),
  });

  render(
    <I18nProvider i18n={i18n}>
      <QueryClientProvider
        client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
      >
        <RouterProvider router={router} />
      </QueryClientProvider>
    </I18nProvider>,
  );

  return { requests };
}

async function signIn() {
  fireEvent.change(await screen.findByRole("textbox", { name: "User name" }), {
    target: { value: "j.berger" },
  });
  fireEvent.change(screen.getByLabelText("Password"), { target: { value: "correct horse" } });
  fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
}

describe("SignInPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("offers no single sign-on while it is off", async () => {
    serve({});

    await screen.findByRole("textbox", { name: "User name" });
    await waitFor(() => {
      expect(screen.queryByRole("link", { name: /^Sign in with/ })).not.toBeInTheDocument();
    });
  });

  it("offers a button for each single sign-on provider that leaves for the server's start", async () => {
    serve({
      "GET /api/auth/external/providers": () =>
        json([{ scheme: "oidc", displayName: "Contoso Entra ID" }]),
    });

    const button = await screen.findByRole("link", { name: "Sign in with Contoso Entra ID" });
    expect(button).toHaveAttribute("href", "/api/auth/external/start?scheme=oidc");
  });

  it("says why a directory account in no mapped group cannot sign in", async () => {
    serve({ "POST /api/auth/login": () => json({ status: "NoRole" }) });

    await signIn();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Your account is in none of the directory groups DDT gives a role to. Ask an administrator.",
    );
  });

  it("says why single sign-on refused an account in no mapped group", async () => {
    serve({}, "/sign-in?error=no-role");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Your account is in none of the groups DDT maps to a role. Ask an administrator.",
    );
  });

  it.each([
    ["external", "The sign-in at the identity provider did not finish. Try again."],
    ["unlinked", "No DDT account is linked to that identity. Ask an administrator."],
    ["provision", "No account could be created for that identity. Ask an administrator."],
    ["locked", "This account is locked. Try again later or ask an administrator."],
    ["not-allowed", "This account may not sign in. Ask an administrator."],
    ["something-new", "The sign-in at the identity provider did not finish. Try again."],
  ])("says what error=%s from single sign-on means", async (error, message) => {
    serve({}, `/sign-in?error=${error}`);

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
  });

  it("goes to the machines after a sign-in", async () => {
    serve({ "POST /api/auth/login": () => json({ status: "Succeeded" }) });

    await signIn();

    expect(await screen.findByText("The machines")).toBeInTheDocument();
  });
});
