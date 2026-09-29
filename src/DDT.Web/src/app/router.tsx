// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { QueryClient } from "@tanstack/react-query";
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  lazyRouteComponent,
  redirect,
} from "@tanstack/react-router";

import { AboutPage } from "@/about/AboutPage";
import { AccountPage } from "@/account/AccountPage";
import { AccountsPage } from "@/accounts/AccountsPage";
import { AuditPage } from "@/audit/AuditPage";
import { currentUserQuery } from "@/auth/auth";
import { BootImagePage } from "@/boot/BootImagePage";
import { ImagesPage } from "@/images/ImagesPage";
import { SignInPage } from "@/auth/SignInPage";
import { MachinePage } from "@/machines/MachinePage";
import { machineSearch, machinesSearch } from "@/machines/machineSearch";
import { MachinesPage } from "@/machines/MachinesPage";
import { DriversPage } from "@/packages/DriversPage";
import { FilesPage } from "@/packages/FilesPage";
import { MachineRolesPage } from "@/roles/MachineRolesPage";
import { rulesSearch } from "@/rules/rules";
import { RulesPage } from "@/rules/RulesPage";
import { ApprovalPage } from "@/settings/ApprovalPage";
import { DeploymentDefaultsPage } from "@/settings/DeploymentDefaultsPage";
import { NetworkBootPage } from "@/settings/NetworkBootPage";
import { ServerPage } from "@/settings/ServerPage";
import { serverSearch } from "@/settings/serverSearch";
import { SignInSettingsPage } from "@/settings/SignInSettingsPage";
import { runHistorySearch } from "@/runs/runHistory";
import { RunHistoryPage } from "@/runs/RunHistoryPage";
import { sequenceSearch, sequencesSearch } from "@/sequences/sequenceSearch";
import { SequencesPage } from "@/sequences/SequencesPage";
import { TokensPage } from "@/tokens/TokensPage";
import { UsersPage } from "@/users/UsersPage";

import { DesignPage } from "./DesignPage";
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

// The server's OpenID Connect callback sends an account with a second factor here to enter its code. It also sends a
// refused sign-in here, with the reason.
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

// The session is checked before the route renders, so a signed-out visitor never sees the app flash. Some accounts
// must replace a password an administrator was shown. They stay on the Account page, the only one the server answers
// for them, until the change updates the cached account.
const shellRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: "shell",
  component: Shell,
  beforeLoad: async ({ context, location }) => {
    const user = await context.queryClient.query({ ...currentUserQuery, staleTime: "static" });

    if (user === null) {
      throw redirect({ to: "/sign-in" });
    }

    if (user.mustChangePassword && location.pathname !== "/account") {
      throw redirect({ to: "/account" });
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

// The pages of the navigation, plus a machine, a sequence and the account.
const machinesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/machines",
  validateSearch: machinesSearch,
  component: MachinesPage,
});
const runHistoryRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/machines/runs",
  validateSearch: runHistorySearch,
  component: RunHistoryPage,
});
const approvalRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/machines/approval",
  component: ApprovalPage,
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
// The flow builder is the only page loaded when it's opened, because it's large and most visits never need it.
const sequenceRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/deployment/sequences/$sequenceId",
  validateSearch: sequenceSearch,
  component: lazyRouteComponent(
    () => import("@/sequences/SequenceEditorPage"),
    "SequenceEditorPage",
  ),
});
const rulesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/deployment/rules",
  validateSearch: rulesSearch,
  component: RulesPage,
});
const machineRolesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/deployment/machine-roles",
  component: MachineRolesPage,
});
const accountsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/deployment/accounts",
  component: AccountsPage,
});
const deploymentDefaultsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/deployment/defaults",
  component: DeploymentDefaultsPage,
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
  component: BootImagePage,
});
const networkBootRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/boot/network",
  component: NetworkBootPage,
});
const usersRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/users",
  component: UsersPage,
});
const signInSettingsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/sign-in",
  component: SignInSettingsPage,
});
const tokensRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/tokens",
  component: TokensPage,
});
const serverRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/server",
  validateSearch: serverSearch,
  component: ServerPage,
});
const auditRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: "/admin/audit",
  component: AuditPage,
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
    machineRolesRoute,
    accountsRoute,
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
