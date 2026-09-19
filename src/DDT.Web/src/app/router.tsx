import type { QueryClient } from "@tanstack/react-query";
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  redirect,
} from "@tanstack/react-router";

import { currentUserQuery } from "@/auth/auth";
import { AccountPage } from "@/pages/AccountPage";
import { ImagesPage } from "@/pages/ImagesPage";
import { MachinesPage } from "@/pages/MachinesPage";
import { SignInPage } from "@/pages/SignInPage";

import { AppShell } from "./AppShell";
import { RootLayout } from "./RootLayout";

export interface RouterContext {
  queryClient: QueryClient;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({ component: RootLayout });

const signInRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/sign-in",
  component: SignInPage,
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
  shellRoute.addChildren([machinesRoute, imagesRoute, accountRoute]),
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
