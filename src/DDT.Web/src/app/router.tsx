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

import { AboutPage } from "@/about/AboutPage";
import { AccountPage } from "@/account/AccountPage";
import { currentUserQuery } from "@/auth/auth";
import { ImagesPage } from "@/images/ImagesPage";
import { SignInPage } from "@/auth/SignInPage";
import { MachinePage } from "@/machines/MachinePage";
import { machineSearch, machinesSearch } from "@/machines/machineSearch";
import { MachinesPage } from "@/machines/MachinesPage";
import { DriversPage, FilesPage } from "@/packages/PackagesPage";
import { RulesPage } from "@/rules/RulesPage";
import { SequenceEditorPage } from "@/sequences/SequenceEditorPage";
import { sequenceSearch, sequencesSearch } from "@/sequences/sequenceSearch";
import { SequencesPage } from "@/sequences/SequencesPage";

import { DesignPage } from "./DesignPage";
import { PendingPage } from "./PendingPage";
import { RouteError } from "./RouteError";
import { RootLayout } from "./RootLayout";
import { Shell } from "./Shell";

export interface RouterContext {
  queryClient: QueryClient;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({
  component: RootLayout,
  errorComponent: RouteError,
});

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

// Everything inside the shell requires a session. The check runs before the route renders, so there is no flash of
// the application for a signed out visitor.
const shellRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: "shell",
  component: Shell,
  beforeLoad: async ({ context }) => {
    const user = await context.queryClient.query({ ...currentUserQuery, staleTime: "static" });

    if (user === null) {
      throw redirect({ to: "/sign-in" });
    }

    return { user };
  },
});

// The server still sends signed in people to /, which is the machine list.
const homeRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/",
  beforeLoad: () => {
    throw redirect({ to: "/machines" });
  },
});

// The pages of the navigation, plus a machine, a sequence and the account. PendingPage stands in until each is rebuilt.
const machinesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/machines",
  validateSearch: machinesSearch,
  component: MachinesPage,
});
const runHistoryRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/machines/runs",
  component: PendingPage,
});
const approvalRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/machines/approval",
  component: PendingPage,
});
const machineRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/machines/$machineId",
  validateSearch: machineSearch,
  component: MachinePage,
});
const sequencesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/deployment/sequences",
  validateSearch: sequencesSearch,
  component: SequencesPage,
});
const sequenceRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/deployment/sequences/$sequenceId",
  validateSearch: sequenceSearch,
  component: SequenceEditorPage,
});
const rulesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/deployment/rules",
  component: RulesPage,
});
const deploymentDefaultsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/deployment/defaults",
  component: PendingPage,
});
const imagesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/library/images",
  component: ImagesPage,
});
const driversRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/library/drivers",
  component: DriversPage,
});
const filesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/library/files",
  component: FilesPage,
});
const bootImageRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/boot/image",
  component: PendingPage,
});
const networkBootRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/boot/network",
  component: PendingPage,
});
const usersRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/users",
  component: PendingPage,
});
const signInSettingsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/sign-in",
  component: PendingPage,
});
const tokensRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/tokens",
  component: PendingPage,
});
const serverRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/server",
  component: PendingPage,
});
const auditRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/audit",
  component: PendingPage,
});
const accountRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/account",
  component: AccountPage,
});

// Every token and component on one page, in development builds only.
const designRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/design",
  component: DesignPage,
});

const routeTree = rootRoute.addChildren([
  signInRoute,
  aboutRoute,
  shellRoute.addChildren([
    homeRoute,
    machinesRoute,
    runHistoryRoute,
    approvalRoute,
    machineRoute,
    sequencesRoute,
    sequenceRoute,
    rulesRoute,
    deploymentDefaultsRoute,
    imagesRoute,
    driversRoute,
    filesRoute,
    bootImageRoute,
    networkBootRoute,
    usersRoute,
    signInSettingsRoute,
    tokensRoute,
    serverRoute,
    auditRoute,
    accountRoute,
    ...(import.meta.env.DEV ? [designRoute] : []),
  ]),
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
