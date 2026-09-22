// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";

import { deploymentOptionsQuery } from "@/deployments/deployments";
import { imagesQuery, type ImageSummary } from "@/images/images";
import { ApiError } from "@/lib/api";
import { useAutosave } from "@/lib/useAutosave";
import { packagesQuery, type PackageSummary } from "@/packages/packages";

import { phasesOf, type Findings } from "./problems";
import {
  changedParts,
  draftOf,
  sameDraft,
  saveRequestOf,
  type SequenceDraft,
} from "./sequenceDraft";
import { isTyping, sequenceEdits, type SequenceEdit } from "./sequenceEdits";
import {
  saveSequence,
  sequenceQuery,
  sequencesQuery,
  type SequenceStep,
  type SequenceView,
} from "./sequences";

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

// One sequence being edited: the draft, saved in place as it changes, and the server's copy, which other
// administrators' saves replace live while this page has no unsaved edits. Read only, nothing changes it.
export function useSequenceEditor(initial: SequenceView, readOnly: boolean) {
  const queryClient = useQueryClient();
  const id = initial.id;

  const stored = useQuery(sequenceQuery(id));
  const images = useQuery(imagesQuery);
  const packages = useQuery(packagesQuery);
  const options = useQuery(deploymentOptionsQuery);

  const autosave = useAutosave<SequenceDraft, SequenceView>({
    initial: { value: draftOf(initial), revision: initial.revision },
    save: (draft, revision, keepalive) =>
      saveSequence(id, saveRequestOf(draft, revision), keepalive),
    savedAs: (view) => ({ value: draftOf(view), revision: view.revision }),
    equals: sameDraft,
    conflicts: true,
    onSaved: (view) => {
      queryClient.setQueryData(sequenceQuery(id).queryKey, view);
      void queryClient.invalidateQueries({ queryKey: sequencesQuery.queryKey });
    },
    onConflict: () => {
      void queryClient.invalidateQueries({ queryKey: sequenceQuery(id).queryKey });
    },
  });

  const [removed, setRemoved] = useState<RemovedStep | null>(null);

  // The newest copy the server gave, with its problems, which the draft is compared against.
  const latest = stored.data ?? initial;
  const deleted = stored.error instanceof ApiError && stored.error.status === 404;
  // A disabled fieldset leaves links focusable, and a card's keys move its step from them too.
  const locked = readOnly || deleted;
  const { receive, stop, update } = autosave;

  useEffect(() => {
    receive(draftOf(latest), latest.revision);
  }, [receive, latest]);

  useEffect(() => {
    if (deleted) {
      stop("This sequence was deleted, so nothing more is saved.");
    }
  }, [stop, deleted]);

  const edit = (change: SequenceEdit) => {
    if (!locked) {
      update((draft) => sequenceEdits(draft, change), !isTyping(change));
    }
  };

  const draft = autosave.value;
  const theirs = autosave.theirs;

  return {
    draft,
    state: autosave.state,
    dirty: autosave.dirty,
    deleted,
    locked,
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
    remove: (stepId: string) => {
      const index = draft.steps.findIndex((step) => step.id === stepId);
      const step = draft.steps[index];

      if (step !== undefined && !locked) {
        edit({ type: "removeStep", id: stepId });
        setRemoved({ step, index });
      }
    },
    undoRemove: () => {
      if (removed !== null) {
        edit({ type: "restoreStep", step: removed.step, index: removed.index });
        setRemoved(null);
      }
    },
    dismissRemoved: () => {
      setRemoved(null);
    },
    takeTheirs: autosave.takeTheirs,
    keepMine: autosave.keepMine,
    flush: autosave.flush,
  };
}
