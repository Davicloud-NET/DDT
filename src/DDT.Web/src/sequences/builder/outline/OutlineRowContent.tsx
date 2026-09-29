// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconChevronRight, IconGripVertical } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { cx } from "@/ui/cx";
import { NodeGlyph } from "@/ui/NodeGlyph";
import { StateTag } from "@/ui/StateTag";

import type { Findings } from "../../problems";
import { findingCounts } from "../../sequenceList";
import { isContainer } from "../../steps";
import type { OutlineRow } from "./outlineRows";

interface OutlineRowContentProps {
  row: OutlineRow;
  title: string;
  // A node's own findings. Null on a Then or an Else row.
  own: Findings | null;
  locked: boolean;
  hasChildItems: boolean;
  isExpanded: boolean;
  level: number;
}

// What a row of the outline shows: its chevron, its place, its kind and title, its drag key and its findings.
export function OutlineRowContent({
  row,
  title,
  own,
  locked,
  hasChildItems,
  isExpanded,
  level,
}: OutlineRowContentProps) {
  const { t } = useLingui();
  const counts = own === null ? null : findingCounts(own.problems.length, own.warnings.length);

  return (
    <div
      className="flex min-h-10 cursor-pointer items-center gap-2 py-1.5 pr-2"
      style={{ paddingLeft: `${String((level - 1) * 1.25 + 0.25)}rem` }}
    >
      {hasChildItems || row.kind === "branch" || isContainer(row.node) ? (
        <AriaButton
          slot="chevron"
          className="flex size-6 shrink-0 cursor-pointer items-center justify-center rounded-key text-muted outline-none hover:bg-hover hover:text-ink"
        >
          <IconChevronRight
            aria-hidden="true"
            size={14}
            stroke={2}
            className={cx("motion-colors", isExpanded && "rotate-90")}
          />
        </AriaButton>
      ) : (
        <span className="w-6 shrink-0" />
      )}
      <span className="w-12 shrink-0 type-small text-muted">{row.path}</span>
      {row.kind === "node" ? <NodeGlyph kind={row.node.kind} className="text-ink-2" /> : null}
      <span
        className={cx(
          "min-w-0 flex-1 truncate",
          row.kind === "branch" ? "type-label text-ink-2" : "type-body text-ink",
        )}
      >
        {title}
      </span>
      {locked ? null : row.kind === "branch" ? (
        // A Then or an Else stays with its IF, but the tree still looks for a drag key.
        <AriaButton slot="drag" isDisabled className="invisible size-7 shrink-0" />
      ) : (
        <AriaButton
          slot="drag"
          aria-label={t`Move ${title}`}
          className="flex size-7 shrink-0 cursor-grab items-center justify-center rounded-key text-control outline-none hover:bg-hover hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
        >
          <IconGripVertical aria-hidden="true" size={14} stroke={2} />
        </AriaButton>
      )}
      {counts === null || own === null ? null : (
        <StateTag tone={own.problems.length > 0 ? "fail" : "attention"} className="h-5">
          {counts}
        </StateTag>
      )}
    </div>
  );
}
