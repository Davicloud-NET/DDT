// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode, RefObject } from "react";
import {
  Header,
  Menu as AriaMenu,
  MenuItem as AriaMenuItem,
  MenuSection as AriaMenuSection,
  Popover,
  Separator,
  type MenuItemProps as AriaMenuItemProps,
  type MenuProps as AriaMenuProps,
  type MenuSectionProps as AriaMenuSectionProps,
} from "react-aria-components";

import { cx } from "./cx";

// A menu in a popover at its trigger. Outside a MenuTrigger, triggerRef names what it opens from, and isOpen
// and onOpenChange say whether it's open. A node's menu, opened by right-click or Shift+F10, works this way.
export function Menu<T extends object>({
  className,
  placement = "bottom end",
  triggerRef,
  isOpen,
  onOpenChange,
  ...props
}: AriaMenuProps<T> & {
  className?: string;
  placement?: "bottom end" | "bottom start";
  triggerRef?: RefObject<Element | null>;
  isOpen?: boolean;
  onOpenChange?: (open: boolean) => void;
}) {
  return (
    <Popover
      placement={placement}
      offset={6}
      {...(triggerRef === undefined ? {} : { triggerRef })}
      {...(isOpen === undefined ? {} : { isOpen })}
      {...(onOpenChange === undefined ? {} : { onOpenChange })}
      className="min-w-56 rounded-overlay bg-raised shadow-overlay outline-none entering:animate-pop-in exiting:animate-pop-out"
    >
      <AriaMenu
        {...props}
        className={cx("max-h-[70vh] overflow-auto p-1.5 outline-none", className)}
      />
    </Popover>
  );
}

export function MenuItem({
  className,
  children,
  ...props
}: Omit<AriaMenuItemProps, "className"> & { className?: string | undefined }) {
  return (
    <AriaMenuItem
      {...props}
      className={cx(
        "flex cursor-pointer items-center gap-2.5 rounded-key px-2.5 py-2 type-body text-ink motion-highlight outline-none",
        "focused:bg-hover disabled:cursor-default disabled:text-muted",
        className,
      )}
    >
      {(renderProps) => (
        <>
          {renderProps.selectionMode !== "none" ? (
            <span aria-hidden="true" className="w-4 shrink-0 text-center type-label">
              {renderProps.isSelected ? "✓" : ""}
            </span>
          ) : null}
          {typeof children === "function" ? children(renderProps) : children}
        </>
      )}
    </AriaMenuItem>
  );
}

export function MenuSection<T extends object>({
  title,
  className,
  children,
  ...props
}: Omit<AriaMenuSectionProps<T>, "children"> & {
  title?: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  return (
    <AriaMenuSection {...props} className={cx("py-1", className)}>
      {title ? <Header className="px-2.5 pb-1 pt-1.5 type-small text-muted">{title}</Header> : null}
      {children}
    </AriaMenuSection>
  );
}

export function MenuSeparator() {
  return <Separator className="my-1 h-px bg-line-soft" />;
}
