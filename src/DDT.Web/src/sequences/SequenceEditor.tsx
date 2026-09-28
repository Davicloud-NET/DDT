// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { Link, useBlocker, useNavigate, useSearch } from "@tanstack/react-router";
import { useEffect, useRef, useState } from "react";

import type { AutosaveState } from "@/lib/autosave";
import { ConfirmDialog } from "@/ui/Dialog";
import { EmptyState, Panel } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { SequenceRailPicker } from "@/ui/SequenceRail";
import { StateTag } from "@/ui/StateTag";

import { AddStepMenu } from "./AddStepMenu";
import { focusField } from "./editorFocus";
import { EditorLock } from "./editorLock";
import { TextSetting } from "./fields";
import { FindingsSummary } from "./FindingsSummary";
import { stepFindings, type Findings } from "./problems";
import { RemovedStepNotice } from "./RemovedStepNotice";
import { SaveState } from "./SaveState";
import { SequenceConflict } from "./SequenceConflict";
import { addStep, insertStepAfter, type SequenceEdit } from "./sequenceEdits";
import type { SequenceStep, SequenceView } from "./sequences";
import { phaseRuns, railSteps } from "./sequenceView";
import { StepInspector } from "./StepInspector";
import { useSequenceEditor } from "./useSequenceEditor";

// The server's refusal of the name or the description, such as a name another sequence has.
function refused(state: AutosaveState, field: string): Findings {
  const messages = state.kind === "refused" ? (state.problem?.errors?.[field] ?? []) : [];

  return { problems: messages.map((message) => ({ stepId: null, field, message })), warnings: [] };
}

function leaveConsequence(state: AutosaveState): string {
  switch (state.kind) {
    case "conflict":
      return t`Someone else saved this sequence, so your changes since your last save are not saved. Leaving throws them away.`;
    case "retrying": {
      const message = state.message;

      return t`${message} Your latest changes are not saved, and leaving throws them away.`;
    }
    case "refused":
    case "stopped": {
      const message = state.message;

      return t`The server did not save your latest changes: ${message} Leaving throws them away.`;
    }
    default:
      return t`Your latest changes are not saved yet. Leaving throws them away.`;
  }
}

// Edits a sequence in place: every change is saved as it is made, and other administrators' saves appear while
// this page has nothing unsaved. The rail at the top is the sequence, one module per step; the step picked on it
// is edited below, beside the sequence's name and every problem the server found.
export function SequenceEditor({
  initial,
  readOnly,
}: {
  initial: SequenceView;
  readOnly: boolean;
}) {
  const { t: translate } = useLingui();
  const editor = useSequenceEditor(initial, readOnly);
  const search = useSearch({ from: "/shell/deployment/sequences/$sequenceId" });
  const navigate = useNavigate({ from: "/deployment/sequences/$sequenceId" });
  const inspector = useRef<HTMLDivElement>(null);
  // Where a finding in the summary sends the focus, once its step shows.
  const pendingFocus = useRef<{ stepId: string; field: string | null } | null>(null);
  const [announcement, setAnnouncement] = useState("");

  // The step shown. The address holds it too, so a reload shows the same step, but it is read only when the page
  // opens: a new address arrives a moment after the edit that chose the step, such as adding it.
  const [shownId, setShownId] = useState(search.step);

  const { draft, state, locked } = editor;
  const steps = draft.steps;
  const found = steps.findIndex((step) => step.id === shownId);
  const index = found < 0 ? 0 : found;
  const selected: SequenceStep | undefined = steps[index];
  const problemCount = editor.findings.problems.length;
  const name = draft.name.trim() === "" ? translate`Unnamed sequence` : draft.name;

  // Leaving saves first and goes once that worked; it asks only when the changes cannot be saved. Picking another
  // step stays on the page, and signing out ends the session a save needs, so neither is held up.
  const leaving = useBlocker({
    shouldBlockFn: async ({ current, next }) =>
      next.pathname !== current.pathname && next.routeId !== "/sign-in" && !(await editor.flush()),
    enableBeforeUnload: false,
    disabled: !editor.dirty,
    withResolver: true,
  });

  useEffect(() => {
    const pending = pendingFocus.current;

    if (pending === null || selected?.id !== pending.stepId) {
      return;
    }

    pendingFocus.current = null;

    if (!focusField(inspector.current, pending.field)) {
      inspector.current?.querySelector<HTMLElement>("[data-findings]")?.focus();
    }
  });

  const select = (stepId: string | undefined) => {
    setShownId(stepId);
    void navigate({
      search: stepId === undefined ? {} : { step: stepId },
      replace: true,
      resetScroll: false,
    });
  };

  const add = (edit: SequenceEdit & { id: string }) => {
    editor.edit(edit);
    select(edit.id);
  };

  const move = (step: SequenceStep, to: number) => {
    editor.edit({ type: "moveStep", id: step.id, to });

    const stepName = step.name;
    const position = to + 1;
    const count = steps.length;

    setAnnouncement(t`${stepName} moved to position ${position} of ${count}.`);
  };

  const remove = (step: SequenceStep) => {
    const at = editor.remove(step.id);

    if (at !== null) {
      const rest = steps.filter((other) => other.id !== step.id);

      select(rest[Math.min(at, rest.length - 1)]?.id);
    }
  };

  const goTo = (stepId: string, field: string | null) => {
    pendingFocus.current = { stepId, field };

    if (selected?.id === stepId) {
      if (!focusField(inspector.current, field)) {
        inspector.current?.querySelector<HTMLElement>("[data-findings]")?.focus();
      }

      pendingFocus.current = null;
    } else {
      select(stepId);
    }
  };

  const phases = phaseRuns(editor.phases).map((run) => ({
    label:
      run.phase === "WindowsPE" ? (
        <Trans>In Windows PE</Trans>
      ) : (
        <Trans>In the installed Windows</Trans>
      ),
    steps: run.steps,
  }));

  return (
    <EditorLock value={locked}>
      <header className="flex flex-wrap items-end gap-x-5 gap-y-2">
        <span className="flex min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-2">
          <h1 className="min-w-0 type-title break-words text-ink">{name}</h1>
          <StateTag tone={problemCount > 0 ? "fail" : "ok"}>
            {problemCount > 0 ? <Trans>Cannot run</Trans> : <Trans>Ready to run</Trans>}
          </StateTag>
        </span>
        {readOnly ? null : (
          <SaveState
            state={state}
            savedElsewhere={editor.savedElsewhere}
            onRetry={() => {
              void editor.flush();
            }}
          />
        )}
      </header>

      {readOnly ? (
        <Notice>
          <Trans>Only administrators change task sequences. You can look at this one.</Trans>
        </Notice>
      ) : null}

      {editor.deleted ? (
        <Notice tone="fail" title={<Trans>This sequence was deleted</Trans>}>
          <Trans>
            Someone deleted it while it was open here, so nothing more is saved.{" "}
            <Link to="/deployment/sequences" className="font-semibold underline">
              Go to the task sequences
            </Link>
            .
          </Trans>
        </Notice>
      ) : null}

      {editor.conflict !== null ? (
        <SequenceConflict
          theirs={editor.conflict.view}
          changes={editor.conflict.changes}
          onTakeTheirs={editor.takeTheirs}
          onKeepMine={editor.keepMine}
        />
      ) : null}

      {editor.flowOnly ? (
        <Notice title={<Trans>This page cannot show this sequence</Trans>}>
          <Trans>
            It uses groups, IF or Repeat nodes, variables, inputs or accounts, which only the flow
            builder shows. Nothing in it is changed here.
          </Trans>
        </Notice>
      ) : (
        <>
          <Panel
            title={<Trans>Steps</Trans>}
            actions={
              locked || steps.length === 0 ? null : (
                <AddStepMenu
                  label={<Trans>Add step</Trans>}
                  fullLabel={translate`Add step at the end`}
                  onAdd={(kind) => {
                    add(addStep(kind));
                  }}
                />
              )
            }
          >
            {steps.length === 0 ? (
              <EmptyState
                className="px-0 py-4"
                title={<Trans>No steps yet</Trans>}
                action={
                  locked ? null : (
                    <AddStepMenu
                      label={<Trans>Add the first step</Trans>}
                      fullLabel={translate`Add the first step`}
                      variant="primary"
                      onAdd={(kind) => {
                        add(addStep(kind));
                      }}
                    />
                  )
                }
              >
                {locked ? (
                  <Trans>This sequence does nothing yet.</Trans>
                ) : (
                  <Trans>
                    A sequence that installs Windows starts by partitioning the disk and then
                    applies an image. One for Linux writes a raw disk image and its cloud-init seed.
                  </Trans>
                )}
              </EmptyState>
            ) : (
              <SequenceRailPicker
                label={translate`Steps of ${name}`}
                steps={railSteps(steps, editor.findings)}
                phases={phases}
                selectedId={selected?.id ?? null}
                onSelect={select}
                {...(locked
                  ? {}
                  : {
                      onMove: (stepId: string, to: number) => {
                        const step = steps.find((candidate) => candidate.id === stepId);

                        if (step !== undefined) {
                          move(step, to);
                        }
                      },
                    })}
              />
            )}
          </Panel>

          {editor.removed !== null ? (
            <RemovedStepNotice
              key={editor.removed.step.id}
              step={editor.removed.step}
              onUndo={() => {
                select(editor.undoRemove()?.id);
              }}
              onDismiss={editor.dismissRemoved}
            />
          ) : null}

          <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
            <div ref={inspector} className="min-w-0">
              {selected !== undefined ? (
                <StepInspector
                  step={selected}
                  index={index}
                  count={steps.length}
                  phase={editor.phases[index] ?? "WindowsPE"}
                  findings={stepFindings(editor.findings, selected.id)}
                  catalog={editor.catalog}
                  onEdit={editor.edit}
                  onMove={(to) => {
                    move(selected, to);
                  }}
                  onInsert={(kind) => {
                    add(insertStepAfter(selected.id, kind));
                  }}
                  onRemove={() => {
                    remove(selected);
                  }}
                />
              ) : null}
            </div>

            <div className="flex min-w-0 flex-col gap-4">
              <Panel title={<Trans>Sequence</Trans>}>
                <TextSetting
                  label={<Trans>Sequence name</Trans>}
                  field="name"
                  findings={refused(state, "name")}
                  value={draft.name}
                  onChange={(text) => {
                    editor.edit({ type: "rename", name: text });
                  }}
                />
                <TextSetting
                  label={<Trans>Description</Trans>}
                  field="description"
                  findings={refused(state, "description")}
                  hint={<Trans>Shown in the list of task sequences.</Trans>}
                  multiline
                  rows={3}
                  value={draft.description}
                  onChange={(text) => {
                    editor.edit({ type: "describe", description: text });
                  }}
                />
              </Panel>
              <FindingsSummary steps={steps} findings={editor.findings} onGoTo={goTo} />
            </div>
          </div>
        </>
      )}

      <p role="status" className="sr-only">
        {announcement}
      </p>

      <ConfirmDialog
        isOpen={leaving.status === "blocked"}
        onOpenChange={(open) => {
          if (!open) {
            leaving.reset?.();
          }
        }}
        title={<Trans>Leave without saving?</Trans>}
        confirmLabel={<Trans>Leave without saving</Trans>}
        danger
        onConfirm={() => {
          leaving.proceed?.();
        }}
      >
        <p>{leaveConsequence(state)}</p>
      </ConfirmDialog>
    </EditorLock>
  );
}
