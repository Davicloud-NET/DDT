// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";
import { Link, Outlet, useNavigate } from "@tanstack/react-router";

import { currentUserQuery, logout } from "@/auth/auth";
import { LiveContext } from "@/live/LiveContext";
import { useLiveUpdates } from "@/live/useLiveUpdates";
import { cx } from "@/lib/cx";
import { ReplacedAnchorBanner } from "@/server/ReplacedAnchorBanner";

import styles from "./AppShell.module.scss";

const navigation = [
  { to: "/", label: "Machines" },
  { to: "/images", label: "Images" },
  { to: "/account", label: "Account" },
] as const;

export function AppShell() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const user = queryClient.getQueryData(currentUserQuery.queryKey) ?? null;

  const live = useLiveUpdates();

  async function signOut() {
    await logout(queryClient);
    await navigate({ to: "/sign-in" });
  }

  return (
    <div className={styles.shell}>
      <aside className={styles.sidebar}>
        <div className={styles.brand}>
          <span className={styles.mark} aria-hidden="true" />
          DDT
        </div>
        <nav className={styles.nav} aria-label="Sections">
          {navigation.map((item) => (
            <Link
              key={item.to}
              to={item.to}
              className={styles.navLink}
              activeProps={{ className: cx(styles.navLink, styles.navLinkActive) }}
              activeOptions={{ exact: true }}
            >
              {item.label}
            </Link>
          ))}
        </nav>
        <div className={styles.account}>
          <span className={styles.accountName}>{user?.displayName ?? user?.userName}</span>
          <button type="button" className={styles.signOut} onClick={() => void signOut()}>
            Sign out
          </button>
          <Link to="/about" className={styles.about}>
            About DDT
          </Link>
        </div>
      </aside>
      <main className={styles.main}>
        <ReplacedAnchorBanner />
        <LiveContext value={live}>
          <Outlet />
        </LiveContext>
      </main>
    </div>
  );
}
