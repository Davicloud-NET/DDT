import { Link, Outlet } from "@tanstack/react-router";

import { cx } from "@/lib/cx";

import styles from "./AppShell.module.scss";

const navigation = [{ to: "/", label: "Machines" }] as const;

export function AppShell() {
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
      </aside>
      <main className={styles.main}>
        <Outlet />
      </main>
    </div>
  );
}
