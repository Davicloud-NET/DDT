// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { ApiError } from "@/lib/api";
import type { AutosaveSnapshot } from "@/lib/autosave";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";

import { indexTree, slotOf, type Slot } from "./flow/flowTree";
import {
  emptyHistory,
  recorded,
  redone,
  undone,
  type History,
  type HistoryCommand,
} from "./flow/history";
import { nodePhasesOf, type Findings } from "./problems";
import { changedParts, draftOf, type SequenceDraft } from "./sequenceDraft";
import { sequenceEdits, typingKey, type SequenceEdit } from "./sequenceEdits";
import { sequenceQuery, type SequenceStep, type SequenceView } from "./sequences";
import { useHistoryKeys } from "./useHistoryKeys";
import { useSequenceAutosave } from "./useSequenceAutosave";
import { useStepCatalog } from "./useStepCatalog";

// A node removed from the flow, and the gap it left, so it can be put back there.
export interface RemovedNode {
  node: SequenceStep;
  slot: Slot;
}

// The node with the gap it leaves when removed. Null if the draft has no such node.
function removal(steps: SequenceStep[], nodeId: string): RemovedNode | null {
  const index = indexTree(steps);
  const node = index.byId.get(nodeId)?.node;
  const slot = slotOf(index, nodeId);

  return node === undefined || slot === undefined ? null : { node, slot };
}

// A removed node's gap, or the end of the flow if its container is gone too.
function restoredSlot(steps: SequenceStep[], slot: Slot): Slot {
  const index = indexTree(steps);

  return slot.parent === null || index.byId.has(slot.parent)
    ? slot
    : {
        parent: null,
        body: "steps",
        index: index.entries.filter((entry) => entry.parent === null).length,
      };
}

// The newer copy someone else saved. This page shows it because it had nothing unsaved.
function savedElsewhereOf(
  latest: SequenceView,
  initialRevision: number,
  ownRevisions: readonly number[],
  me: string | null,
): { by: string | null; at: string } | null {
  return latest.revision !== initialRevision &&
    !ownRevisions.includes(latest.revision) &&
    (me === null || latest.updatedBy !== me)
    ? { by: latest.updatedBy, at: latest.updatedUtc }
    : null;
}

// The copy another administrator saved, while this page's own edits haven't been saved over it.
function conflictOf(autosave: AutosaveSnapshot<SequenceDraft>, latest: SequenceView) {
  const theirs = autosave.theirs;

  return autosave.state.kind === "conflict"
    ? {
        view: theirs !== null && latest.revision === theirs.revision ? latest : null,
        changes: theirs === null ? [] : changedParts(autosave.base, theirs.value),
      }
    : null;
}

// One sequence being edited. It holds the draft, which is saved as it changes, and the server's copy. Other
// administrators' saves replace that copy live while nothing is unsaved. When read-only, nothing changes it.
export function useSequenceEditor(first: SequenceView, readOnly: boolean) {
  // The copy the page opened with. Later copies arrive through the query, so a new one from the caller is ignored.
  const [initial] = useState(first);
  const freshness = liveListOptions(useLiveStatus());
  const stored = useQuery({ ...sequenceQuery(initial.id), ...freshness });
  const catalog = useStepCatalog(freshness);
  const me = useQuery(currentUserQuery).data?.userName ?? null;
  // Undo and redo, dropped once the page shows someone else's copy.
  const [history, setHistory] = useState<History<SequenceDraft>>(emptyHistory);
  const { autosave, ownRevisions } = useSequenceAutosave(initial, () => {
    setHistory(emptyHistory());
  });

  // The newest copy the server gave, with its problems, which the draft is compared against.
  const latest = stored.data ?? initial;
  const deleted = stored.error instanceof ApiError && stored.error.status === 404;
  const draft = autosave.value;
  const locked = readOnly || deleted;
  // The newest draft, for a key pressed long after the render that made it, such as a toast's Undo.
  const newest = useRef(draft);

  useEffect(() => {
    newest.current = draft;
  });
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

  // Goes one step back or forward. It's saved at once, like any edit that isn't typing.
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

  useHistoryKeys(step);

  return {
    draft,
    state: autosave.state,
    dirty: autosave.dirty,
    deleted,
    locked,
    savedElsewhere: savedElsewhereOf(latest, initial.revision, ownRevisions, me),
    findings: { problems: latest.problems, warnings: latest.warnings } satisfies Findings,
    phases: nodePhasesOf(draft.steps, {
      steps: latest.definition.steps,
      stepPhases: latest.stepPhases,
      nodePhases: latest.nodePhases,
    }),
    catalog,
    conflict: conflictOf(autosave, latest),
    edit,
    undo: () => {
      step("undo");
    },
    redo: () => {
      step("redo");
    },
    canUndo: !locked && history.past.length > 0,
    canRedo: !locked && history.future.length > 0,
    // Removes a node from the flow, wherever it is, and returns it with the gap it left.
    remove: (nodeId: string): RemovedNode | null => {
      const removed = locked ? null : removal(draft.steps, nodeId);

      if (removed !== null) {
        edit({ type: "removeNodes", ids: [nodeId] });
      }

      return removed;
    },
    // Puts a removed node back in its gap, or at the end if its container is gone too.
    restore: ({ node, slot }: RemovedNode) => {
      edit({ type: "insertNodes", slot: restoredSlot(newest.current.steps, slot), nodes: [node] });
    },
    takeTheirs: autosave.takeTheirs,
    keepMine: autosave.keepMine,
    flush: autosave.flush,
  };
}

export type SequenceEditorState = ReturnType<typeof useSequenceEditor>;
