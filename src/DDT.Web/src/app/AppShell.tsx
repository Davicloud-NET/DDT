import { useQueryClient } from "@tanstack/react-query";
import { Link, Outlet, useNavigate } from "@tanstack/react-router";

import { currentUserQuery, logout } from "@/auth/auth";
import { useLiveUpdates } from "@/live/useLiveUpdates";
import { cx } from "@/lib/cx";

import styles from "./AppShell.module.scss";

const navigation = [
  { to: "/", label: "Machines" },
  { to: "/account", label: "Account" },
] as const;

export function AppShell() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const user = queryClient.getQueryData(currentUserQuery.queryKey) ?? null;

  useLiveUpdates();

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
        </div>
      </aside>
      <main className={styles.main}>
        <Outlet />
      </main>
    </div>
  );
}
