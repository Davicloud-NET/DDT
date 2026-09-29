// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { FilterSelector } from "@/ui/FilterSelector";
import { StateTag } from "@/ui/StateTag";

import { SaveState } from "../../SaveState";
import type { SequenceEditorState } from "../../useSequenceEditor";
import { UndoRedo } from "./UndoRedo";

interface FlowHeaderProps {
  name: string;
  editor: SequenceEditorState;
  readOnly: boolean;
  phone: boolean;
  view: "flow" | "outline";
  onView: (view: "flow" | "outline") => void;
}

// The sequence's name and whether it can run, with the save state, undo and redo, and the choice of view.
export function FlowHeader({ name, editor, readOnly, phone, view, onView }: FlowHeaderProps) {
  const { t } = useLingui();
  const problemCount = editor.findings.problems.length;

  return (
    <header className="flex flex-wrap items-end justify-between gap-x-5 gap-y-3">
      <span className="flex min-w-0 flex-wrap items-center gap-x-3.5 gap-y-2">
        <h1 className="min-w-0 type-title break-words text-ink">{name}</h1>
        <StateTag tone={problemCount > 0 ? "fail" : "ok"}>
          {problemCount > 0 ? (
            plural(problemCount, { one: "# problem", other: "# problems" })
          ) : (
            <Trans>Ready to run</Trans>
          )}
        </StateTag>
      </span>
      <div className="flex flex-wrap items-center gap-3">
        {readOnly ? null : (
          <SaveState
            state={editor.state}
            savedElsewhere={editor.savedElsewhere}
            onRetry={() => {
              void editor.flush();
            }}
          />
        )}
        {editor.locked ? null : <UndoRedo editor={editor} />}
        {phone ? null : (
          <FilterSelector
            label={t`View`}
            options={[
              { id: "flow", label: <Trans>Flow</Trans> },
              { id: "outline", label: <Trans>Outline</Trans> },
            ]}
            selected={view}
            onChange={(id) => {
              onView(id === "outline" ? "outline" : "flow");
            }}
          />
        )}
      </div>
    </header>
  );
}
