import { createRootRoute, createRoute, createRouter } from "@tanstack/react-router";

import { MachinesPage } from "@/pages/MachinesPage";

import { AppShell } from "./AppShell";

const rootRoute = createRootRoute({ component: AppShell });

const machinesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: MachinesPage,
});

const routeTree = rootRoute.addChildren([machinesRoute]);

export const router = createRouter({
  routeTree,
  defaultPreload: "intent",
  scrollRestoration: true,
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
