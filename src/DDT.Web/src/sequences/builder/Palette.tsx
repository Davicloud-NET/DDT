// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useState } from "react";
import { Input, SearchField } from "react-aria-components";

import { cx } from "@/ui/cx";
import { fieldClass } from "@/ui/TextField";

import type { StepKind } from "../sequences";
import type { FlowDrag } from "./flowDrag";
import { addLabel, flowKinds, workKinds } from "./nodeKinds";
import { PaletteItem } from "./PaletteItem";

interface PaletteProps {
  onAdd: (kind: StepKind) => void;
  onDragChange: (drag: FlowDrag) => void;
  className?: string;
}

// What can be added to the flow, to drag onto a wire. With the keyboard, Enter picks one up, Tab goes from wire to
// wire, and Enter puts it down. A click adds it after the chosen node.
export function Palette({ onAdd, onDragChange, className }: PaletteProps) {
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
