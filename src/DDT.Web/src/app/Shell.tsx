// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, Outlet, useNavigate, useRouterState } from "@tanstack/react-router";
import { useEffect, useState, useSyncExternalStore } from "react";
import {
  Button as AriaButton,
  MenuTrigger,
  SharedElement,
  SharedElementTransition,
  type Key,
  type Selection,
} from "react-aria-components";

import { currentUserQuery, logout, type CurrentUser } from "@/auth/auth";
import { chooseLanguage, LANGUAGES, type Language } from "@/i18n/i18n";
import { LiveContext } from "@/live/LiveContext";
import type { LiveConnection } from "@/live/liveConnection";
import { useLiveUpdates } from "@/live/useLiveUpdates";
import { cx } from "@/ui/cx";
import { Logo } from "@/ui/Logo";
import { Menu, MenuItem, MenuSection, MenuSeparator } from "@/ui/Menu";
import { useReplay } from "@/ui/motion";
import { Toasts } from "@/ui/Toast";

import { CommandPalette } from "./CommandPalette";
import { categories, locate } from "./navigation";
import { chooseTheme, useThemeChoice, type ThemeChoice } from "./theme";

// An account signed in with a password an administrator was shown reaches only its Account page until it has set its
// own: the server answers nothing else, and the router sends every other address there. Meanwhile the shell offers no
// navigation, search or live connection, and brings them back as soon as the cached account says the password changed.
export function Shell() {
  const passwordFirst = useQuery(currentUserQuery).data?.mustChangePassword === true;
  const live = useLiveUpdates(!passwordFirst);
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const here = passwordFirst ? null : locate(pathname);
  // Another page fades in; the first one, and the same page with another filter, simply show.
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
      <Toasts />
    </div>
  );
}

function TopBar({
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
        <Logo size={24} className="text-frame-logo" />
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

// The pages of a category. The underline of the page shown moves to the next page chosen in the same row; another
// category's row starts with its own.
function SubNavigation({ categoryId, activePage }: { categoryId: string; activePage: string }) {
  const { i18n } = useLingui();
  const category = categories.find((candidate) => candidate.id === categoryId);

  if (!category) {
    return null;
  }

  return (
    <nav
      aria-label={i18n._(category.label)}
      className="flex h-11 shrink-0 items-stretch gap-6.5 overflow-x-auto border-b border-line px-6"
    >
      <SharedElementTransition key={category.id}>
        {category.pages.map((page) => {
          const active = page.to === activePage;

          return (
            <Link
              key={page.to}
              to={page.to}
              aria-current={active ? "page" : undefined}
              className={cx(
                "relative flex shrink-0 items-center type-label motion-colors outline-none focus-visible:outline-2 focus-visible:-outline-offset-2",
                active ? "text-ink" : "font-medium text-ink-2 hover:text-ink",
              )}
            >
              {i18n._(page.label)}
              {/* Only its position moves; it takes the new page's width at once. */}
              <SharedElement
                name="page-underline"
                isVisible={active}
                aria-hidden="true"
                className="absolute inset-x-0 bottom-0 h-0.5 bg-ink transition-[translate] duration-(--duration-normal) ease-standard"
              />
            </Link>
          );
        })}
      </SharedElementTransition>
    </nav>
  );
}

// The name to show: the display name when the account has one, else the user name.
function shownName(user: CurrentUser): string {
  const display = user.displayName?.trim();

  return display !== undefined && display !== "" ? display : user.userName;
}

function initials(user: CurrentUser): string {
  const name = shownName(user);
  const parts = name.split(/[\s.@_-]+/).filter(Boolean);

  return ((parts[0]?.[0] ?? "") + (parts[1]?.[0] ?? "")).toUpperCase() || "?";
}

function UserMenu({ user }: { user: CurrentUser }) {
  const { i18n, t } = useLingui();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const theme = useThemeChoice();
  const name = shownName(user);

  async function signOut() {
    await logout(queryClient);
    await navigate({ to: "/sign-in" });
  }

  function pick(selection: Selection, apply: (key: Key) => void) {
    if (selection !== "all") {
      const [key] = selection;

      if (key !== undefined) {
        apply(key);
      }
    }
  }

  return (
    <MenuTrigger>
      <AriaButton
        aria-label={t`Account menu for ${name}`}
        className="w-13 cursor-pointer border-l border-frame-line type-label text-frame-text motion-colors outline-none hover:bg-frame-hover focus-visible:outline-2 focus-visible:-outline-offset-2"
      >
        {initials(user)}
      </AriaButton>
      <Menu aria-label={t`Account menu`}>
        <MenuSection title={<span className="text-ink">{name}</span>}>
          <MenuItem href="/account">
            <Trans>Account and security</Trans>
          </MenuItem>
        </MenuSection>
        <MenuSeparator />
        <MenuSection
          title={<Trans>Language</Trans>}
          selectionMode="single"
          disallowEmptySelection
          selectedKeys={[i18n.locale]}
          onSelectionChange={(selection) => {
            pick(selection, (key) => void chooseLanguage(String(key) as Language));
          }}
        >
          {Object.entries(LANGUAGES).map(([code, label]) => (
            <MenuItem key={code} id={code} lang={code}>
              {label}
            </MenuItem>
          ))}
          {import.meta.env.DEV ? (
            <MenuItem id="pseudo">
              <Trans>Pseudo (development)</Trans>
            </MenuItem>
          ) : null}
        </MenuSection>
        <MenuSeparator />
        <MenuSection
          title={<Trans>Appearance</Trans>}
          selectionMode="single"
          disallowEmptySelection
          selectedKeys={[theme]}
          onSelectionChange={(selection) => {
            pick(selection, (key) => {
              chooseTheme(String(key) as ThemeChoice);
            });
          }}
        >
          <MenuItem id="system">
            <Trans>Same as the system</Trans>
          </MenuItem>
          <MenuItem id="light">
            <Trans>Light</Trans>
          </MenuItem>
          <MenuItem id="dark">
            <Trans>Dark</Trans>
          </MenuItem>
        </MenuSection>
        <MenuSeparator />
        <MenuSection>
          <MenuItem href="/about">
            <Trans>About DDT</Trans>
          </MenuItem>
          <MenuItem onAction={() => void signOut()}>
            <Trans>Sign out</Trans>
          </MenuItem>
        </MenuSection>
      </Menu>
    </MenuTrigger>
  );
}

// Nothing shows while the live connection is up. Only a connection lost for a few seconds earns a banner, so a
// short reconnect or the first connect never flashes one.
function ConnectionBanner({ live }: { live: LiveConnection }) {
  const status = useSyncExternalStore(live.onStatusChange, live.status);
  const [shown, setShown] = useState(false);

  useEffect(() => {
    const live = status === "live";
    const timer = window.setTimeout(
      () => {
        setShown(!live);
      },
      live ? 0 : 4000,
    );

    return () => {
      window.clearTimeout(timer);
    };
  }, [status]);

  if (!shown) {
    return null;
  }

  return (
    <div
      role="status"
      className="shrink-0 border-b border-line bg-attention px-6 py-2 type-small text-on-attention"
    >
      <Trans>
        The live connection to the server is lost. DDT keeps trying to reconnect; until then, pages
        refresh every few seconds.
      </Trans>
    </div>
  );
}
