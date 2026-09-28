// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconGripVertical } from "@tabler/icons-react";
import { useState, type MouseEvent } from "react";
import { Input, SearchField, useDrag } from "react-aria-components";

import { cx } from "@/ui/cx";
import { NodeGlyph } from "@/ui/FlowNode";
import { fieldClass } from "@/ui/TextField";

import type { StepKind } from "../sequences";
import { addLabel, flowKinds, workKinds } from "./nodeKinds";
import { KIND_TYPE, type FlowDrag } from "./FlowCanvas";

// What can be added to the flow, to drag onto a wire. The keyboard picks one up with Enter, goes from wire to wire with
// Tab and puts it down with Enter; a click adds it after the chosen node.
export function Palette({
  onAdd,
  onDragChange,
  className,
}: {
  onAdd: (kind: StepKind) => void;
  onDragChange: (drag: FlowDrag) => void;
  className?: string;
}) {
  const { t } = useLingui();
  const [find, setFind] = useState("");
  const matches = (kind: StepKind) =>
    addLabel(kind).toLowerCase().includes(find.trim().toLowerCase());
  const sections = [
    { title: t`Flow`, kinds: flowKinds.filter(matches) },
    { title: t`Steps`, kinds: workKinds.filter(matches) },
  ].filter((section) => section.kinds.length > 0);

  return (
    <aside
      aria-label={t`Add to the flow`}
      className={cx(
        "flex min-h-0 flex-col gap-3 rounded-panel bg-panel px-3 py-4 shadow-panel",
        className,
      )}
    >
      <h2 className="mx-1 type-heading text-ink">
        <Trans>Add</Trans>
      </h2>
      <SearchField aria-label={t`Find a step`} value={find} onChange={setFind} className="mx-1">
        <Input placeholder={t`Find a step`} className={cx(fieldClass, "h-8.5 type-body")} />
      </SearchField>
      <div className="flex min-h-0 flex-1 flex-col gap-0.5 overflow-y-auto">
        {sections.map((section) => (
          <section key={section.title} aria-label={section.title} className="flex flex-col gap-0.5">
            <h3 className="mx-1 mt-1 mb-1 type-small text-muted">{section.title}</h3>
            {section.kinds.map((kind) => (
              <PaletteItem key={kind} kind={kind} onAdd={onAdd} onDragChange={onDragChange} />
            ))}
          </section>
        ))}
        {sections.length === 0 ? (
          <p className="mx-1 type-small text-muted">
            <Trans>Nothing has that name.</Trans>
          </p>
        ) : null}
      </div>
      <p className="mx-1 type-small text-muted">
        <Trans>Drag onto a wire, or choose + on one. Arrow keys follow the flow.</Trans>
      </p>
    </aside>
  );
}

function PaletteItem({
  kind,
  onAdd,
  onDragChange,
}: {
  kind: StepKind;
  onAdd: (kind: StepKind) => void;
  onDragChange: (drag: FlowDrag) => void;
}) {
  const label = addLabel(kind);
  const { dragProps, isDragging } = useDrag({
    getItems: () => [{ [KIND_TYPE]: kind, "text/plain": label }],
    getAllowedDropOperations: () => ["copy"],
    onDragStart: () => {
      onDragChange({ kind });
    },
    onDragEnd: () => {
      onDragChange(null);
    },
  });

  return (
    <button
      type="button"
      {...dragProps}
      className={cx(
        "flex h-8.5 shrink-0 cursor-grab items-center gap-2.5 rounded-key px-2 text-left type-body text-ink motion-colors outline-none",
        "hover:bg-hover focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus",
        isDragging && "bg-selected",
      )}
      onClick={(event: MouseEvent<HTMLButtonElement>) => {
        dragProps.onClick?.(event);

        // A screen reader's click starts a drag; a pointer's adds the kind.
        if (!event.defaultPrevented) {
          onAdd(kind);
        }
      }}
    >
      <NodeGlyph kind={kind} className="text-ink-2" />
      <span className="flex-1 truncate">{label}</span>
      <IconGripVertical aria-hidden="true" size={14} stroke={2} className="shrink-0 text-control" />
    </button>
  );
}
