// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The tabs of Administration > Server. The shown tab is in the URL, so the overview can link to a tab and a
// reload stays on it. The overview tab itself has no parameter.
export const serverTabs = ["overview", "certificate", "proxies", "agent", "logging"] as const;

export type ServerTab = (typeof serverTabs)[number];

export interface ServerSearch {
  tab?: Exclude<ServerTab, "overview">;
}

function isServerTab(value: unknown): value is ServerTab {
  return serverTabs.some((tab) => tab === value);
}

export function serverSearch(search: Record<string, unknown>): ServerSearch {
  const tab = search.tab;

  return isServerTab(tab) && tab !== "overview" ? { tab } : {};
}
