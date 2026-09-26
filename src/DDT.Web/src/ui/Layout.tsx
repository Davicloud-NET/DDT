// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";

// The parts pages are built from.

// A page's content area: the same gutters on every page.
export function Page({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx("flex flex-col gap-4 px-6 pt-5 pb-8", className)}>{children}</div>;
}

// The page title with what sits beside it: filters, a search field, the page's main action.
export function PageHeader({
  title,
  children,
  className,
}: {
  title: ReactNode;
  children?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cx("flex flex-wrap items-center gap-x-5 gap-y-3", className)}>
      <h1 className="type-title text-ink">{title}</h1>
      {children}
    </div>
  );
}

// A raised surface, one step lighter than the page, edged with a hairline. Panels cast no shadow.
export function Panel({
  children,
  title,
  actions,
  className,
  flush = false,
}: {
  children: ReactNode;
  title?: ReactNode;
  actions?: ReactNode;
  className?: string;
  // Without inner padding, for tables and lists that run to the panel's edges.
  flush?: boolean;
}) {
  return (
    <section className={cx("flex flex-col rounded-panel bg-panel shadow-panel", className)}>
      {title || actions ? (
        <div
          className={cx(
            "flex items-center gap-3",
            flush ? "border-b border-line-soft px-4 py-3" : "px-4 pt-4",
          )}
        >
          {title ? (
            <h2 className="flex-1 type-heading text-ink">{title}</h2>
          ) : (
            <span className="flex-1" />
          )}
          {actions}
        </div>
      ) : null}
      <div className={cx("flex min-h-0 flex-1 flex-col", !flush && "gap-3 p-4")}>{children}</div>
    </section>
  );
}

// What a place shows when there is nothing in it yet: what it will hold, and how to put something there.
export function EmptyState({
  title,
  children,
  action,
  className,
}: {
  title: ReactNode;
  children?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cx("flex flex-col items-start gap-2 px-4 py-8", className)}>
      <p className="type-heading text-ink">{title}</p>
      {children ? <p className="max-w-[60ch] text-ink-2">{children}</p> : null}
      {action ? <div className="pt-2">{action}</div> : null}
    </div>
  );
}

// A placeholder in the shape of what is loading, so the page does not jump when it arrives.
export function Skeleton({ className }: { className?: string }) {
  return (
    <span aria-hidden="true" className={cx("block animate-pulse rounded-tag bg-well", className)} />
  );
}

// Facts as label and value pairs. "plate" lays them out in a row of cells, like the plate on a machine.
export function Facts({
  items,
  layout = "list",
  className,
}: {
  items: { label: ReactNode; value: ReactNode; mono?: boolean }[];
  layout?: "list" | "plate";
  className?: string;
}) {
  // The cells of a plate sit on a hairline-coloured ground one pixel apart, so the rules between them follow the
  // cells when they wrap onto more rows on a narrow screen.
  if (layout === "plate") {
    return (
      <dl
        className={cx(
          "grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-px overflow-hidden rounded-panel bg-line-soft shadow-panel",
          className,
        )}
      >
        {items.map((item, index) => (
          <div key={index} className="flex min-w-0 flex-col gap-0.75 bg-panel px-3.5 py-2.5">
            <dt className="type-small text-muted">{item.label}</dt>
            <dd className={cx("truncate", item.mono ? "type-data" : "type-body")}>{item.value}</dd>
          </div>
        ))}
      </dl>
    );
  }

  return (
    <dl
      className={cx(
        "grid grid-cols-[minmax(7rem,auto)_minmax(0,1fr)] gap-x-3 gap-y-1.5",
        className,
      )}
    >
      {items.map((item, index) => (
        <div key={index} className="contents">
          <dt className="type-small leading-[1.45] text-muted">{item.label}</dt>
          <dd className={cx("min-w-0 break-words", item.mono ? "type-data" : "type-body")}>
            {item.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}
