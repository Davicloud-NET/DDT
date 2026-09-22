// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useBlocker } from "@tanstack/react-router";
import { useRef, useState } from "react";

import { ConfirmDialog } from "@/components/ConfirmDialog";
import type { AutosaveState } from "@/lib/autosave";

import { AddStep } from "./AddStep";
import { FindingList } from "./FindingList";
import { sequenceFindings } from "./problems";
import { RemovedStepNotice } from "./RemovedStepNotice";
import { SaveStatus } from "./SaveStatus";
import { SequenceConflict } from "./SequenceConflict";
import { addStep } from "./sequenceEdits";
import { SequenceHeader } from "./SequenceHeader";
import type { SequenceView } from "./sequences";
import { StepList } from "./StepList";
import { useSequenceEditor } from "./useSequenceEditor";

import styles from "./SequenceEditor.module.scss";

export interface SequenceEditorProps {
  // The copy the page opened with. The parent keys the editor by the sequence.
  initial: SequenceView;
  readOnly: boolean;
}

function refusedField(state: AutosaveState, field: string): string[] {
  return state.kind === "refused" ? (state.problem?.errors?.[field] ?? []) : [];
}

function leaveConsequence(state: AutosaveState): string {
  switch (state.kind) {
    case "conflict":
      return "Someone else saved this sequence, so your changes since your last save are not saved. Leaving throws them away.";
    case "retrying":
      return `${state.message} Your latest changes are not saved, and leaving throws them away.`;
    case "refused":
    case "stopped":
      return `The server did not save your latest changes: ${state.message} Leaving throws them away.`;
    default:
      return "Your latest changes are not saved yet. Leaving throws them away.";
  }
}

// Edits a sequence in place: every change is saved as it is made, and other administrators' saves appear
// while this page has nothing unsaved.
export function SequenceEditor({ initial, readOnly }: SequenceEditorProps) {
  const editor = useSequenceEditor(initial);
  const container = useRef<HTMLDivElement>(null);
  const [announcement, setAnnouncement] = useState("");

  const { draft, state } = editor;

  // Leaving saves first and goes once that worked; it asks only when the changes cannot be saved. Signing out
  // ends the session a save needs, so it is never held up.
  const leaving = useBlocker({
    shouldBlockFn: async ({ next }) => next.routeId !== "/sign-in" && !(await editor.flush()),
    enableBeforeUnload: false,
    disabled: !editor.dirty,
    withResolver: true,
  });

  const problemCount = editor.findings.problems.length;
  const warningCount = editor.findings.warnings.length;

  return (
    <div ref={container} className={styles.editor}>
      <SaveStatus state={state} readOnly={readOnly} />

      {editor.conflict !== null && (
        <SequenceConflict
          theirs={editor.conflict.view}
          changes={editor.conflict.changes}
          onTakeTheirs={editor.takeTheirs}
          onKeepMine={editor.keepMine}
        />
      )}

      {editor.deleted && (
        <p className={styles.deleted} role="alert">
          This sequence was deleted, so nothing more is saved.
        </p>
      )}

      <fieldset className={styles.body} disabled={readOnly || editor.deleted}>
        <SequenceHeader
          name={draft.name}
          description={draft.description}
          nameMessages={refusedField(state, "name")}
          descriptionMessages={refusedField(state, "description")}
          problemCount={problemCount}
          warningCount={warningCount}
          onRename={(name) => {
            editor.edit({ type: "rename", name });
          }}
          onDescribe={(description) => {
            editor.edit({ type: "describe", description });
          }}
          onShowFirstFinding={() => {
            container.current
              ?.querySelector<HTMLElement>("[data-finding], [aria-invalid='true']")
              ?.focus();
          }}
        />

        <FindingList findings={sequenceFindings(editor.findings, draft.steps)} />

        {draft.steps.length === 0 ? (
          <p className={styles.empty}>
            This sequence has no steps yet. Add the first one below. A sequence that installs
            Windows starts by partitioning the disk and then applies an image.
          </p>
        ) : (
          <StepList
            steps={draft.steps}
            phases={editor.phases}
            findings={editor.findings}
            catalog={editor.catalog}
            onEdit={editor.edit}
            onRemove={editor.remove}
            onAnnounce={setAnnouncement}
          />
        )}

        {editor.removed !== null && (
          <RemovedStepNotice
            key={editor.removed.step.id}
            step={editor.removed.step}
            onUndo={editor.undoRemove}
            onDismiss={editor.dismissRemoved}
          />
        )}

        <AddStep
          action="Add step"
          kindLabel="Kind of step to add at the end"
          onAdd={(kind) => {
            editor.edit(addStep(kind));
          }}
        />
      </fieldset>

      <p className={styles.announcement} role="status">
        {announcement}
      </p>

      {leaving.status === "blocked" && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              leaving.reset();
            }
          }}
          title="Leave without saving?"
          consequence={leaveConsequence(state)}
          confirmLabel="Leave without saving"
          busy={false}
          error={null}
          onConfirm={() => {
            leaving.proceed();
          }}
        />
      )}
    </div>
  );
}
