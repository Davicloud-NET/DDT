// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.
// Adapted from the table of Untitled UI React, Copyright (c) 2025 Untitled UI, MIT, see licenses/untitledui/LICENSE.

import { IconArrowDown, IconSelector } from "@tabler/icons-react";
import type { ReactNode } from "react";
import {
  Cell as AriaCell,
  Collection,
  Column as AriaColumn,
  Row as AriaRow,
  Table as AriaTable,
  TableBody as AriaTableBody,
  TableHeader as AriaTableHeader,
  useTableOptions,
  type CellProps as AriaCellProps,
  type ColumnProps as AriaColumnProps,
  type RowProps as AriaRowProps,
  type TableBodyProps as AriaTableBodyProps,
  type TableHeaderProps as AriaTableHeaderProps,
  type TableProps as AriaTableProps,
} from "react-aria-components";

import { Checkbox } from "./Checkbox";
import { cx } from "./cx";

// A data table on React Aria: keyboard navigation between cells, row selection with boxes, and sorting by column.
// It sits on a panel; rows are divided by hairlines drawn as pseudo-elements, so they take no space, and the
// selected row takes the selection tone.

export function Table({
  className,
  ...props
}: Omit<AriaTableProps, "className"> & { className?: string }) {
  return (
    <div className="overflow-x-auto">
      <AriaTable {...props} className={cx("w-full border-separate border-spacing-0", className)} />
    </div>
  );
}

export function TableHeader<T extends object>({
  columns,
  children,
  className,
  ...props
}: Omit<AriaTableHeaderProps<T>, "className"> & { className?: string }) {
  const { selectionBehavior, selectionMode } = useTableOptions();

  return (
    <AriaTableHeader {...props} className={cx("h-10", className)}>
      {selectionBehavior === "toggle" ? (
        <AriaColumn className="w-11 border-b border-line pl-4">
          {selectionMode === "multiple" ? <Checkbox slot="selection" /> : null}
        </AriaColumn>
      ) : null}
      <Collection {...(columns === undefined ? {} : { items: columns })}>{children}</Collection>
    </AriaTableHeader>
  );
}

export interface TableColumnProps extends Omit<AriaColumnProps, "className" | "children"> {
  children?: ReactNode;
  className?: string;
}

export function TableColumn({ children, className, ...props }: TableColumnProps) {
  return (
    <AriaColumn
      {...props}
      className={cx(
        "border-b border-line px-3 text-left align-middle type-label text-ink-2 outline-none",
        "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus",
        props.allowsSorting && "cursor-pointer hover:text-ink",
        className,
      )}
    >
      {({ allowsSorting, sortDirection }) => (
        <span className="inline-flex items-center gap-1 whitespace-nowrap">
          {children}
          {allowsSorting ? (
            sortDirection ? (
              <IconArrowDown
                aria-hidden="true"
                size={14}
                stroke={2.5}
                className={cx("text-ink", sortDirection === "ascending" && "rotate-180")}
              />
            ) : (
              <IconSelector aria-hidden="true" size={14} stroke={2} className="text-muted" />
            )
          ) : null}
        </span>
      )}
    </AriaColumn>
  );
}

export function TableBody<T extends object>({
  className,
  ...props
}: Omit<AriaTableBodyProps<T>, "className"> & { className?: string }) {
  return <AriaTableBody {...props} className={cx("", className)} />;
}

export function TableRow<T extends object>({
  columns,
  children,
  className,
  ...props
}: Omit<AriaRowProps<T>, "className"> & { className?: string }) {
  const { selectionBehavior } = useTableOptions();

  return (
    <AriaRow
      {...props}
      className={cx(
        "group/row h-14 outline-none transition-colors hover:bg-hover selected:bg-selected",
        "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus",
        props.href || props.onAction ? "cursor-pointer" : "",
        className,
      )}
    >
      {selectionBehavior === "toggle" ? (
        <AriaCell className="border-b border-line-soft pl-4 group-last/row:border-b-0">
          <Checkbox slot="selection" />
        </AriaCell>
      ) : null}
      <Collection {...(columns === undefined ? {} : { items: columns })}>{children}</Collection>
    </AriaRow>
  );
}

export function TableCell({
  className,
  ...props
}: Omit<AriaCellProps, "className"> & { className?: string }) {
  return (
    <AriaCell
      {...props}
      className={cx(
        "border-b border-line-soft px-3 py-2 align-middle type-body text-ink outline-none group-last/row:border-b-0",
        "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus",
        className,
      )}
    />
  );
}
