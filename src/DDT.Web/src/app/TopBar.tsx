// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";

import { currentUserQuery } from "@/auth/auth";
import { cx } from "@/ui/cx";
import { Logo } from "@/ui/Logo";

import { CommandPalette } from "./CommandPalette";
import { categories } from "./navigation";
import { UserMenu } from "./UserMenu";

export function TopBar({
  activeCategory,
  passwordFirst,
}: {
  activeCategory: string | undefined;
  passwordFirst: boolean;
}) {
  const { i18n, t } = useLingui();
  const user = useQueryClient().getQueryData(currentUserQuery.queryKey) ?? null;

  return (
    <header className="flex h-13 shrink-0 items-stretch bg-frame pl-5 text-frame-text">
      <Link
        to="/machines"
        className="flex shrink-0 items-center gap-2.5 pr-6 outline-none focus-visible:outline-2 max-sm:pr-3"
      >
        <Logo size={22} />
        <span className="type-wordmark text-frame-text">DDT</span>
      </Link>
      <nav
        aria-label={t`Sections`}
        className="flex min-w-0 flex-1 items-end gap-0.5 overflow-x-auto [scrollbar-width:none]"
      >
        {(passwordFirst ? [] : categories).map((category) => {
          const active = category.id === activeCategory;

          return (
            <Link
              key={category.id}
              to={category.pages[0].to}
              aria-current={active ? "page" : undefined}
              className={cx(
                "flex h-10.5 shrink-0 items-center rounded-t-key px-4 type-label whitespace-nowrap motion-colors outline-none focus-visible:outline-2 focus-visible:-outline-offset-2",
                active ? "bg-page text-ink" : "text-frame-muted hover:text-frame-text",
              )}
            >
              {i18n._(category.label)}
            </Link>
          );
        })}
      </nav>
      {passwordFirst ? null : <CommandPalette />}
      {user ? <UserMenu user={user} /> : null}
    </header>
  );
}
