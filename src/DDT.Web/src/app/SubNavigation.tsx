// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";
import { SharedElement, SharedElementTransition } from "react-aria-components";

import { cx } from "@/ui/cx";

import { categories } from "./navigation";

// The pages of a category. The underline of the page shown moves to the next page chosen in the same row; another
// category's row starts with its own.
export function SubNavigation({
  categoryId,
  activePage,
}: {
  categoryId: string;
  activePage: string;
}) {
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
