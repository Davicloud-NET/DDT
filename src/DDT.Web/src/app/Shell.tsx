// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { Outlet, useRouterState } from "@tanstack/react-router";

import { currentUserQuery } from "@/auth/auth";
import { LiveContext } from "@/live/LiveContext";
import { useLiveUpdates } from "@/live/useLiveUpdates";
import { cx } from "@/ui/cx";
import { useReplay } from "@/ui/motion";
import { ToastRegion } from "@/ui/ToastRegion";

import { ConnectionBanner } from "./ConnectionBanner";
import { locate } from "./navigation";
import { SubNavigation } from "./SubNavigation";
import { TopBar } from "./TopBar";

// An account that signed in with a password an administrator was shown must set its own first. Until then, the
// server only answers its Account page, so the shell leaves out navigation, search and the live connection.
export function Shell() {
  const passwordFirst = useQuery(currentUserQuery).data?.mustChangePassword === true;
  const live = useLiveUpdates(!passwordFirst);
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const here = passwordFirst ? null : locate(pathname);
  // A new page fades in. The first page, and the same page with another filter, just appear.
  const replay = useReplay(pathname);

  return (
    <div className="flex h-full flex-col">
      <TopBar activeCategory={here?.category.id} passwordFirst={passwordFirst} />
      {here ? <SubNavigation categoryId={here.category.id} activePage={here.page.to} /> : null}
      {passwordFirst ? null : <ConnectionBanner live={live} />}
      <main
        className={cx(
          "min-h-0 flex-1 overflow-auto",
          replay === 0 && "animate-page-in",
          replay === 1 && "animate-page-in-again",
        )}
      >
        <LiveContext value={live}>
          <Outlet />
        </LiveContext>
      </main>
      <ToastRegion />
    </div>
  );
}
