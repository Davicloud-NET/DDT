// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconDots } from "@tabler/icons-react";
import type { ReactNode } from "react";
import { Button as AriaButton, MenuTrigger, type Key } from "react-aria-components";

import { cx } from "./cx";
import { Menu } from "./Menu";

// The "…" button of a list's row, which opens the row's actions.
export function RowActionsMenu({
  label,
  triggerLabel = label,
  isDisabled,
  disabledKeys,
  onAction,
  className,
  children,
}: {
  // Names the menu, and the button unless triggerLabel names it.
  label: string;
  triggerLabel?: string;
  isDisabled?: boolean;
  disabledKeys?: Iterable<Key>;
  onAction: (key: Key) => void;
  // The button's size or its place in the row's grid.
  className?: string;
  children: ReactNode;
}) {
  return (
    <MenuTrigger>
      <AriaButton
        aria-label={triggerLabel}
        {...(isDisabled === undefined ? {} : { isDisabled })}
        className={cx(
          "flex size-7.5 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus",
          className,
        )}
      >
        <IconDots size={18} stroke={2} />
      </AriaButton>
      <Menu
        aria-label={label}
        {...(disabledKeys === undefined ? {} : { disabledKeys })}
        onAction={onAction}
      >
        {children}
      </Menu>
    </MenuTrigger>
  );
}
