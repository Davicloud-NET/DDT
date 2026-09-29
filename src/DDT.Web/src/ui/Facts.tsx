// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { cx } from "./cx";

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
            <dd className={cx("min-w-0 break-words", item.mono ? "type-data" : "type-body")}>
              {item.value}
            </dd>
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
