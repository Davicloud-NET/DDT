// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { QueryClient } from "@tanstack/react-query";
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  redirect,
} from "@tanstack/react-router";

import { currentUserQuery } from "@/auth/auth";
import { machineSearch } from "@/machines/machineSearch";
import { AboutPage } from "@/pages/AboutPage";
import { AccountPage } from "@/pages/AccountPage";
import { ImagesPage } from "@/pages/ImagesPage";
import { MachineDetailPage } from "@/pages/MachineDetailPage";
import { MachinesPage } from "@/pages/MachinesPage";
import { SignInPage } from "@/pages/SignInPage";

import { AppShell } from "./AppShell";
import { RootLayout } from "./RootLayout";

export interface RouterContext {
  queryClient: QueryClient;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({ component: RootLayout });

// The server's OpenID Connect callback sends an account with a second factor here for its code, and a sign-in it
// refused with the reason.
const signInRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/sign-in",
  validateSearch: (search: Record<string, unknown>): { step?: "two-factor"; error?: string } => ({
    ...(search.step === "two-factor" ? { step: "two-factor" as const } : {}),
    ...(typeof search.error === "string" ? { error: search.error } : {}),
  }),
  component: SignInPage,
});

// Outside the shell, so the licence notices can be read without signing in.
const aboutRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/about",
  component: AboutPage,
});

// Everything inside the shell requires a session. The check runs before the route renders, so
// there is no flash of the application for a signed out visitor.
const shellRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: "shell",
  component: AppShell,
  beforeLoad: async ({ context }) => {
    const user = await context.queryClient.query({ ...currentUserQuery, staleTime: "static" });

    if (user === null) {
      throw redirect({ to: "/sign-in" });
    }

    return { user };
  },
});

const machinesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/",
  component: MachinesPage,
});

const machineRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/machines/$machineId",
  validateSearch: machineSearch,
  component: MachineDetailPage,
});

const imagesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/images",
  component: ImagesPage,
});

const accountRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/account",
  component: AccountPage,
});

const routeTree = rootRoute.addChildren([
  signInRoute,
  aboutRoute,
  shellRoute.addChildren([machinesRoute, machineRoute, imagesRoute, accountRoute]),
]);

export function createAppRouter(queryClient: QueryClient) {
  return createRouter({
    routeTree,
    context: { queryClient },
    defaultPreload: "intent",
    scrollRestoration: true,
  });
}

declare module "@tanstack/react-router" {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
