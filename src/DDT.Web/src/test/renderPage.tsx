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
  Link,
  Outlet,
  RouterProvider,
} from "@tanstack/react-router";
import { act, render } from "@testing-library/react";
import type { ReactNode } from "react";
import { onTestFinished, vi } from "vitest";

import { AccountPage } from "@/account/AccountPage";
import { AccountsPage } from "@/accounts/AccountsPage";
import { CommandPalette } from "@/app/CommandPalette";
import { RootLayout } from "@/app/RootLayout";
import type { CurrentUser } from "@/auth/auth";
import { ImagesPage } from "@/images/ImagesPage";
import { LiveContext } from "@/live/LiveContext";
import { MachinePage } from "@/machines/MachinePage";
import { machineSearch, machinesSearch } from "@/machines/machineSearch";
import { MachinesPage } from "@/machines/MachinesPage";
import { DriversPage } from "@/packages/DriversPage";
import { FilesPage } from "@/packages/FilesPage";
import { MachineRolesPage } from "@/roles/MachineRolesPage";
import { rulesSearch } from "@/rules/rules";
import { RulesPage } from "@/rules/RulesPage";

import { testHub, type TestHub } from "./fakeHub";
import { serve, type Routes, type TestServer } from "./server";
import { settle } from "./settle";

export interface PageOptions {
  // Where the page opens, such as "/machines?selected=m1".
  path: string;
  user: CurrentUser | null;
  routes?: Routes;
  // Whether the live connection is up. Without it, the pages read their lists on a timer.
  live?: boolean;
  // A phone's width, where the machine pages lay out differently.
  narrow?: boolean;
  // Puts the command palette's button in the frame, as the shell does.
  palette?: boolean;
}

export interface RenderedPage {
  server: TestServer;
  queryClient: QueryClient;
  router: ReturnType<typeof testRouter>;
  // Null without a live connection.
  hub: TestHub | null;
}

// A page as the app shows it: in the router, under a frame with a link away, and with the live connection. The
// routes use the app's route ids, because the pages read their parameters by them.
function testRouter(path: string, hub: TestHub | null, palette: boolean) {
  const rootRoute = createRootRoute({ component: RootLayout });
  const shellRoute = createRoute({
    getParentRoute: () => rootRoute,
    id: "shell",
    component: () => (
      <>
        <header>
          <nav aria-label="Sections">
            <Link to="/about">About DDT</Link>
          </nav>
          {palette ? <CommandPalette /> : null}
        </header>
        <main>
          <LiveContext value={hub?.live ?? null}>
            <Outlet />
          </LiveContext>
        </main>
      </>
    ),
  });
  const page = (at: string, component: () => ReactNode) =>
    createRoute({ getParentRoute: () => shellRoute, path: at, component });
  const stub = (at: string, title: string) => page(at, () => <h1>{title}</h1>);

  return createRouter({
    routeTree: rootRoute.addChildren([
      shellRoute.addChildren([
        createRoute({
          getParentRoute: () => shellRoute,
          path: "/machines",
          validateSearch: machinesSearch,
          component: MachinesPage,
        }),
        createRoute({
          getParentRoute: () => shellRoute,
          path: "/machines/$machineId",
          validateSearch: machineSearch,
          component: MachinePage,
        }),
        createRoute({
          getParentRoute: () => shellRoute,
          path: "/deployment/rules",
          validateSearch: rulesSearch,
          component: RulesPage,
        }),
        page("/deployment/machine-roles", MachineRolesPage),
        page("/deployment/accounts", AccountsPage),
        page("/library/images", ImagesPage),
        page("/library/drivers", DriversPage),
        page("/library/files", FilesPage),
        page("/account", AccountPage),
        stub("/about", "About DDT"),
        stub("/deployment/sequences", "Task sequences"),
        stub("/deployment/sequences/$sequenceId", "A task sequence"),
      ]),
    ]),
    history: createMemoryHistory({ initialEntries: [path] }),
  });
}

// Renders a page against a fake server and, unless told otherwise, a live connection that is up. Everything is
// undone when the test ends.
export async function renderPage(options: PageOptions): Promise<RenderedPage> {
  const server = serve(options.user, options.routes ?? {});
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const hub = options.live === false ? null : testHub(queryClient);
  const narrow = options.narrow === true;

  // The router scrolls to the top after a navigation, and the pages ask how wide the window is; jsdom does
  // neither.
  vi.stubGlobal("scrollTo", vi.fn());
  vi.stubGlobal(
    "matchMedia",
    vi.fn((query: string) => ({
      matches: narrow && query.includes("max-width"),
      media: query,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    })),
  );

  onTestFinished(() => {
    hub?.live.stop();
    queryClient.clear();
    vi.unstubAllGlobals();
  });

  if (hub !== null) {
    await act(async () => {
      hub.live.start();
      await settle();
    });
  }

  const router = testRouter(options.path, hub, options.palette === true);

  render(
    <I18nProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </I18nProvider>,
  );

  return { server, queryClient, router, hub };
}
