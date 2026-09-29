// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { insertCopies } from "../../flow/flowEdits";
import { shiftEdit, slotAfter, type FlowCommand } from "../../flow/flowKeyboard";
import { nodeTitle } from "../../flow/flowLabels";
import type { TreeIndex } from "../../flow/flowTree";
import type { SequenceStep } from "../../sequences";
import type { NodeAction } from "../canvas/nodeAction";
import type { EditHandlers } from "./editHandlers";
import type { FlowSelection } from "./useFlowSelection";

interface CommandContext {
  index: TreeIndex;
  phone: boolean;
  selection: Pick<FlowSelection, "select" | "show">;
  edits: EditHandlers;
  clipboard: {
    copy: (nodes: SequenceStep[]) => void;
    paste: (after: string | null) => Promise<void>;
  };
  announce: (text: string) => void;
  openDrawer: () => void;
  focusWhenShown: (field: string) => void;
  toggleCollapsed: (id: string) => void;
  addAfter: (id: string) => void;
}

// What the canvas's keys and a node's menu do on the page, and what they tell a screen reader.
export function commandHandlers(context: CommandContext) {
  const { index, selection, edits, clipboard, announce } = context;

  const command = (given: FlowCommand, id: string) => {
    const entry = index.byId.get(id);

    if (entry === undefined) {
      return;
    }

    const node = entry.node;
    const title = nodeTitle(node);

    switch (given.type) {
      case "open":
        selection.select(id);

        if (context.phone) {
          context.openDrawer();
        } else {
          context.focusWhenShown("name");
        }

        break;
      case "remove":
        edits.remove(id);
        break;
      case "copy":
        clipboard.copy([node]);
        announce(t`Copied ${title}.`);
        break;
      case "cut":
        clipboard.copy([node]);
        edits.remove(id, false);
        announce(t`Cut ${title}.`);
        break;
      case "paste":
        void clipboard.paste(id);
        break;
      case "duplicate": {
        const insert = insertCopies(slotAfter(index, id), [node]);
        const [copied] = insert.nodes;

        edits.edit(insert);

        if (copied !== undefined) {
          selection.show(copied.id);
        }

        announce(t`Duplicated ${title}.`);
        break;
      }
      case "shift": {
        const move = shiftEdit(index, id, given.by);

        if (move !== null) {
          const position = entry.index + 1 + given.by;
          const count = index.entries.filter(
            (other) => other.parent === entry.parent && other.body === entry.body,
          ).length;

          edits.edit(move);
          announce(t`${title} moved to position ${position} of ${count}.`);
        }

        break;
      }
      case "move":
      case "menu":
        break;
    }
  };

  const action = (given: NodeAction, id: string) => {
    switch (given.type) {
      case "wrap":
        edits.wrap(id, given.kind);
        break;
      case "unwrap":
        edits.unwrap(id);
        break;
      case "collapse":
        context.toggleCollapsed(id);
        break;
      case "addAfter":
        context.addAfter(id);
        break;
      case "command":
        command(given.command, id);
        break;
    }
  };

  return { command, action };
}
