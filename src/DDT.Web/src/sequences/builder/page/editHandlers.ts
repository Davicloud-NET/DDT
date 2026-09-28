// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { showToast } from "@/ui/toasts";

import { insertNode, wrapIn } from "../../flow/flowEdits";
import { afterRemoval } from "../../flow/flowKeyboard";
import { nodeTitle } from "../../flow/flowLabels";
import { bodiesOf, type Slot, type TreeIndex } from "../../flow/flowTree";
import type { SequenceEdit } from "../../sequenceEdits";
import type { ContainerKind, StepKind } from "../../sequences";
import type { SequenceEditorState } from "../../useSequenceEditor";
import { addLabel } from "../nodeKinds";
import type { FlowSelection } from "./useFlowSelection";

// How long a removed node can be brought back from its toast; Ctrl+Z brings it back for longer.
const UNDO_MS = 10_000;

interface EditContext {
  editor: SequenceEditorState;
  index: TreeIndex;
  selection: Pick<FlowSelection, "select" | "show">;
  announce: (text: string) => void;
}

// The page's edits of the flow, each leaving the node it touched chosen and shown.
export function editHandlers({ editor, index, selection, announce }: EditContext) {
  const { select, show } = selection;

  const edit = (change: SequenceEdit) => {
    editor.edit(change);
  };

  const add = (slot: Slot, kind: StepKind) => {
    const insert = insertNode(slot, kind);
    const [node] = insert.nodes;

    edit(insert);

    if (node !== undefined) {
      const label = addLabel(kind);

      show(node.id);
      announce(t`Added ${label}.`);
    }
  };

  const remove = (id: string, notify = true) => {
    const next = afterRemoval(index, id);
    const removed = editor.remove(id);

    if (removed === null) {
      return;
    }

    const title = nodeTitle(removed.node);

    if (next === null) {
      select(null);
    } else {
      show(next);
    }

    if (notify) {
      showToast(
        {
          title: t`Removed ${title}.`,
          action: {
            label: t`Undo`,
            onAction: () => {
              editor.restore(removed);
              show(removed.node.id);
            },
          },
        },
        UNDO_MS,
      );
    }
  };

  const wrap = (id: string, kind: ContainerKind) => {
    const wrapping = wrapIn([id], kind);

    edit(wrapping);
    show(wrapping.container.id);
  };

  const unwrap = (id: string) => {
    const node = index.byId.get(id)?.node;
    const first = node === undefined ? undefined : bodiesOf(node).flatMap((body) => body.steps)[0];

    edit({ type: "unwrapNode", id });

    if (first !== undefined) {
      show(first.id);
    }
  };

  const move = (ids: string[], slot: Slot) => {
    edit({ type: "moveNodes", ids, slot });

    const [first] = ids;

    if (first !== undefined) {
      show(first);
    }
  };

  return { edit, add, remove, wrap, unwrap, move };
}

export type EditHandlers = ReturnType<typeof editHandlers>;
