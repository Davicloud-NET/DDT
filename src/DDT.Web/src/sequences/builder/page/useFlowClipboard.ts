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

// The system clipboard, if the browser lets the page use it. The builder also keeps its own copy.
function systemClipboard(): Clipboard | undefined {
  return (navigator as { clipboard?: Clipboard }).clipboard;
}

// Copies and pastes nodes through the system clipboard. The page's own copy is used if the browser refuses access.
export function useFlowClipboard(
  index: TreeIndex,
  edit: (change: SequenceEdit) => void,
  show: (id: string) => void,
  announce: (text: string) => void,
) {
  // What Ctrl+C copied, for a browser that doesn't let the page read the system clipboard.
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
      // The browser doesn't let the page read the clipboard, so the builder's own copy is used.
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
