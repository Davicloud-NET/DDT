// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  Tab as AriaTab,
  TabList as AriaTabList,
  TabPanel as AriaTabPanel,
  Tabs as AriaTabs,
  type TabListProps as AriaTabListProps,
  type TabPanelProps as AriaTabPanelProps,
  type TabProps as AriaTabProps,
  type TabsProps as AriaTabsProps,
} from "react-aria-components";

import { cx } from "./cx";

// Tabs within a page look like the page row under the top bar: the chosen one is underlined in ink.
export function Tabs({
  className,
  ...props
}: Omit<AriaTabsProps, "className"> & { className?: string }) {
  return <AriaTabs {...props} className={cx("flex flex-col gap-4", className)} />;
}

export function TabList<T extends object>({
  className,
  ...props
}: Omit<AriaTabListProps<T>, "className"> & { className?: string }) {
  return <AriaTabList {...props} className={cx("flex gap-6 border-b border-line", className)} />;
}

export function Tab({
  className,
  ...props
}: Omit<AriaTabProps, "className"> & { className?: string }) {
  return (
    <AriaTab
      {...props}
      className={cx(
        "-mb-px flex h-10 cursor-pointer items-center type-label font-medium text-ink-2 outline-none hover:text-ink",
        "selected:font-semibold selected:text-ink selected:shadow-[inset_0_-2px_0_var(--color-ink)]",
        "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus",
        className,
      )}
    />
  );
}

export function TabPanel({
  className,
  ...props
}: Omit<AriaTabPanelProps, "className"> & { className?: string }) {
  return <AriaTabPanel {...props} className={cx("outline-none", className)} />;
}
