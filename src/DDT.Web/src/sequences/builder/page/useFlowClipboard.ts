// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { useRef } from "react";

import { clipboardText, nodesFromClipboard } from "../../flow/flowClipboard";
import { insertCopies } from "../../flow/flowEdits";
import { slotAfter } from "../../flow/flowKeyboard";
import type { TreeIndex } from "../../flow/flowTree";
import type { SequenceEdit } from "../../sequenceEdits";
import type { SequenceStep } from "../../sequences";

// The system clipboard where the browser lets the page use it; the builder keeps its own copy beside it.
function systemClipboard(): Clipboard | undefined {
  return (navigator as { clipboard?: Clipboard }).clipboard;
}

// Copy and paste of nodes through the system clipboard, with the page's own copy where the browser refuses it.
export function useFlowClipboard(
  index: TreeIndex,
  edit: (change: SequenceEdit) => void,
  show: (id: string) => void,
  announce: (text: string) => void,
) {
  // What Ctrl+C copied, for a browser that keeps the system clipboard from the page.
  const clipboard = useRef<SequenceStep[] | null>(null);

  const copy = (nodes: SequenceStep[]) => {
    clipboard.current = nodes;
    void systemClipboard()
      ?.writeText(clipboardText(nodes))
      .catch(() => undefined);
  };

  const paste = async (after: string | null) => {
    let nodes = clipboard.current;

    try {
      const text = await systemClipboard()?.readText();
      const read = text === undefined ? null : nodesFromClipboard(text);

      nodes = read ?? nodes;
    } catch {
      // The browser keeps the clipboard from the page; the builder's own copy is used.
    }

    if (nodes === null) {
      announce(t`Nothing to paste. Copy a step first.`);
      return;
    }

    const insert = insertCopies(slotAfter(index, after), nodes);
    const [first] = insert.nodes;
    const count = insert.nodes.length;

    edit(insert);

    if (first !== undefined) {
      show(first.id);
    }

    announce(plural(count, { one: "Pasted # step.", other: "Pasted # steps." }));
  };

  return { copy, paste };
}
