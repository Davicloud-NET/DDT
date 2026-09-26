// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Outlet, useRouter } from "@tanstack/react-router";
import { RouterProvider } from "react-aria-components";

declare module "react-aria-components" {
  interface RouterConfig {
    href: string;
  }
}

// React Aria's links and menu items navigate through the application's router instead of reloading the page.
export function RootLayout() {
  const router = useRouter();

  return (
    <RouterProvider
      navigate={(to) => void router.navigate({ to })}
      useHref={(to) => router.buildLocation({ to }).href}
    >
      <Outlet />
    </RouterProvider>
  );
}
