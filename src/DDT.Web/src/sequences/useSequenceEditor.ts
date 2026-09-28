// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { deploymentOptionsQuery } from "@/deployments/deployments";
import { imagesQuery, type ImageSummary } from "@/images/images";
import { ApiError } from "@/lib/api";
import { useAutosave } from "@/lib/useAutosave";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { packagesQuery, type PackageSummary } from "@/packages/packages";

import {
  emptyHistory,
  historyCommand,
  isTextField,
  recorded,
  redone,
  undone,
  type History,
  type HistoryCommand,
} from "./flow/history";
import { phasesOf, type Findings } from "./problems";
import {
  changedParts,
  draftOf,
  needsFlowBuilder,
  sameDraft,
  saveRequestOf,
  type SequenceDraft,
} from "./sequenceDraft";
import { sequenceEdits, typingKey, type SequenceEdit } from "./sequenceEdits";
import { upsertSummary } from "./sequenceList";
import { saveSequence, sequenceQuery, type SequenceStep, type SequenceView } from "./sequences";

// What the step fields choose from.
export interface StepCatalog {
  images: ImageSummary[];
  packages: PackageSummary[];
  // Null until the server's settings are read.
  domainConfigured: boolean | null;
}

export interface RemovedStep {
  step: SequenceStep;
  index: number;
}

// A 409 answers with the copy the server holds, so the page need not read it again.
function isView(body: unknown): body is SequenceView {
  return (
    typeof body === "object" &&
    body !== null &&
    "id" in body &&
    "revision" in body &&
    "definition" in body
  );
}

// One sequence being edited: the draft, saved in place as it changes, and the server's copy, which other
// administrators' saves replace live while this page has no unsaved edits. Read only, nothing changes it. The hub
// says when someone saves; while it cannot, the copy is read every few seconds.
export function useSequenceEditor(first: SequenceView, readOnly: boolean) {
  const queryClient = useQueryClient();
  // The copy the page opened with. Later copies arrive through the query, so a new one from the caller is ignored.
  const [initial] = useState(first);
  const id = initial.id;
  const freshness = liveListOptions(useLiveStatus());

  const stored = useQuery({ ...sequenceQuery(id), ...freshness });
  const images = useQuery({ ...imagesQuery, ...freshness });
  const packages = useQuery({ ...packagesQuery, ...freshness });
  const options = useQuery(deploymentOptionsQuery);
  const me = useQuery(currentUserQuery).data?.userName ?? null;

  // The revisions this page's own saves made, so a copy the hub brings in is told apart from someone else's.
  const [ownRevisions, setOwnRevisions] = useState<readonly number[]>([]);
  // Undo and redo, dropped once the page shows someone else's copy.
  const [history, setHistory] = useState<History<SequenceDraft>>(emptyHistory);

  const autosave = useAutosave<SequenceDraft, SequenceView>({
    initial: { value: draftOf(initial), revision: initial.revision },
    save: async (draft, revision, keepalive) => {
      try {
        return await saveSequence(id, saveRequestOf(draft, revision), keepalive);
      } catch (error) {
        // Someone else saved first. Their copy comes with the refusal; only without it is it read.
        if (error instanceof ApiError && error.status === 409) {
          if (isView(error.problem)) {
            queryClient.setQueryData(sequenceQuery(id).queryKey, error.problem);
          } else {
            void queryClient.invalidateQueries({ queryKey: sequenceQuery(id).queryKey });
          }
        }

        throw error;
      }
    },
    savedAs: (view) => ({ value: draftOf(view), revision: view.revision }),
    equals: sameDraft,
    conflicts: true,
    onSaved: (view) => {
      setOwnRevisions((revisions) => [...revisions, view.revision]);
      queryClient.setQueryData(sequenceQuery(id).queryKey, view);
      upsertSummary(queryClient, view);
    },
    onTakenIn: () => {
      setHistory(emptyHistory());
    },
  });

  const [removed, setRemoved] = useState<RemovedStep | null>(null);

  // The newest copy the server gave, with its problems, which the draft is compared against.
  const latest = stored.data ?? initial;
  const deleted = stored.error instanceof ApiError && stored.error.status === 404;
  const draft = autosave.value;
  // A sequence with groups, variables and the like is left as it is, also when someone else's save makes it one.
  const flowOnly = needsFlowBuilder(draft);
  const locked = readOnly || deleted || flowOnly;
  const { receive, stop, update } = autosave;

  useEffect(() => {
    receive(draftOf(latest), latest.revision);
  }, [receive, latest]);

  useEffect(() => {
    if (deleted) {
      stop(t`This sequence was deleted, so nothing more is saved.`);
    }
  }, [stop, deleted]);

  const edit = (change: SequenceEdit) => {
    if (locked) {
      return;
    }

    const typing = typingKey(change);

    update((current) => {
      const next = sequenceEdits(current, change);

      if (next !== current) {
        setHistory((before) => recorded(before, current, typing, Date.now()));
      }

      return next;
    }, typing === null);
  };

  // Goes a step back or forward, saved at once like any edit that is not typing.
  const step = (direction: HistoryCommand) => {
    if (locked) {
      return;
    }

    update((current) => {
      const stepped = direction === "undo" ? undone(history, current) : redone(history, current);

      if (stepped === null) {
        return current;
      }

      setHistory(stepped.history);

      return stepped.value;
    }, true);
  };

  // Ctrl+Z and Ctrl+Y anywhere on the page but in a text field, whose own undo takes back its typing.
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const command = historyCommand(event);

      if (command === null || event.defaultPrevented || isTextField(event.target)) {
        return;
      }

      event.preventDefault();
      step(command);
    };

    document.addEventListener("keydown", onKeyDown);

    return () => {
      document.removeEventListener("keydown", onKeyDown);
    };
  });

  const theirs = autosave.theirs;
  // The newer copy someone else saved, which this page shows since it had nothing unsaved.
  const savedElsewhere =
    latest.revision !== initial.revision &&
    !ownRevisions.includes(latest.revision) &&
    (me === null || latest.updatedBy !== me)
      ? { by: latest.updatedBy, at: latest.updatedUtc }
      : null;

  return {
    draft,
    state: autosave.state,
    dirty: autosave.dirty,
    deleted,
    locked,
    flowOnly,
    savedElsewhere,
    findings: { problems: latest.problems, warnings: latest.warnings } satisfies Findings,
    phases: phasesOf(draft.steps, latest.definition.steps, latest.stepPhases),
    catalog: {
      images: images.data ?? [],
      packages: packages.data ?? [],
      domainConfigured: options.data?.domainConfigured ?? null,
    } satisfies StepCatalog,
    // The copy another administrator saved, while this page's own edits are not saved over it.
    conflict:
      autosave.state.kind === "conflict"
        ? {
            view: theirs !== null && latest.revision === theirs.revision ? latest : null,
            changes: theirs === null ? [] : changedParts(autosave.base, theirs.value),
          }
        : null,
    removed,
    edit,
    undo: () => {
      step("undo");
    },
    redo: () => {
      step("redo");
    },
    canUndo: !locked && history.past.length > 0,
    canRedo: !locked && history.future.length > 0,
    // Answers the place the step had, so the page can show the step that takes it.
    remove: (stepId: string): number | null => {
      const index = draft.steps.findIndex((step) => step.id === stepId);
      const step = draft.steps[index];

      if (step === undefined || locked) {
        return null;
      }

      edit({ type: "removeStep", id: stepId });
      setRemoved({ step, index });

      return index;
    },
    // Answers the step it brought back.
    undoRemove: (): SequenceStep | null => {
      if (removed === null) {
        return null;
      }

      edit({ type: "restoreStep", step: removed.step, index: removed.index });
      setRemoved(null);

      return removed.step;
    },
    dismissRemoved: () => {
      setRemoved(null);
    },
    takeTheirs: autosave.takeTheirs,
    keepMine: autosave.keepMine,
    flush: autosave.flush,
  };
}

export type SequenceEditorState = ReturnType<typeof useSequenceEditor>;
