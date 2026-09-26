// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";

// The top bar holds five categories, each with its own row of pages. A new feature adds a page to a category,
// never a tab to the bar. Settings sit with the thing they configure.
export interface NavigationPage {
  to: string;
  label: MessageDescriptor;
}

export interface NavigationCategory {
  id: string;
  label: MessageDescriptor;
  pages: [NavigationPage, ...NavigationPage[]];
}

export const categories: NavigationCategory[] = [
  {
    id: "machines",
    label: msg`Machines`,
    pages: [
      { to: "/machines", label: msg`All machines` },
      { to: "/machines/runs", label: msg`Run history` },
      { to: "/machines/approval", label: msg`Approval and zero touch` },
    ],
  },
  {
    id: "deployment",
    label: msg`Deployment`,
    pages: [
      { to: "/deployment/sequences", label: msg`Task sequences` },
      { to: "/deployment/rules", label: msg`Assignment rules` },
      { to: "/deployment/defaults", label: msg`Deployment defaults` },
    ],
  },
  {
    id: "library",
    label: msg`Library`,
    pages: [
      { to: "/library/images", label: msg`OS images` },
      { to: "/library/drivers", label: msg`Drivers` },
      { to: "/library/files", label: msg`Files` },
    ],
  },
  {
    id: "boot",
    label: msg`Boot`,
    pages: [
      { to: "/boot/image", label: msg`Boot image` },
      { to: "/boot/network", label: msg`Network boot` },
    ],
  },
  {
    id: "admin",
    label: msg`Administration`,
    pages: [
      { to: "/admin/users", label: msg`Users and roles` },
      { to: "/admin/sign-in", label: msg`Sign-in` },
      { to: "/admin/tokens", label: msg`API tokens` },
      { to: "/admin/server", label: msg`Server` },
      { to: "/admin/audit", label: msg`Audit log` },
    ],
  },
];

function matches(pathname: string, to: string): boolean {
  return pathname === to || pathname.startsWith(`${to}/`);
}

// The category and page a path belongs to. The longest matching page wins, so /machines/runs is Run history while
// a machine's own page, /machines/<id>, stays under All machines.
export function locate(
  pathname: string,
): { category: NavigationCategory; page: NavigationPage } | null {
  for (const category of categories) {
    const page = category.pages
      .filter((candidate) => matches(pathname, candidate.to))
      .sort((a, b) => b.to.length - a.to.length)[0];

    if (page) {
      return { category, page };
    }
  }

  return null;
}
