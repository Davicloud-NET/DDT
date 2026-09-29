// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { Button as AriaButton, MenuTrigger, type Key, type Selection } from "react-aria-components";

import { logout, type CurrentUser } from "@/auth/auth";
import { chooseLanguage, LANGUAGES, type Language } from "@/i18n/i18n";
import { Menu, MenuItem, MenuSection, MenuSeparator } from "@/ui/Menu";

import { chooseTheme, useThemeChoice, type ThemeChoice } from "./theme";

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

export function UserMenu({ user }: { user: CurrentUser }) {
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
