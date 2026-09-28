// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import { isStepKind } from "../steps";

// What Ctrl+C puts on the clipboard: the nodes as the document holds them, marked as DDT's, so a paste can tell them
// from other text.
export interface FlowClipboard {
  ddtFlow: 1;
  nodes: SequenceStep[];
}

export function clipboardText(nodes: readonly SequenceStep[]): string {
  return JSON.stringify({ ddtFlow: 1, nodes: [...nodes] } satisfies FlowClipboard);
}

function isNode(value: unknown): value is SequenceStep {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const node = value as Record<string, unknown>;

  if (
    typeof node.kind !== "string" ||
    !isStepKind(node.kind) ||
    typeof node.id !== "string" ||
    typeof node.name !== "string" ||
    !Array.isArray(node.conditions) ||
    typeof node.continueOnError !== "boolean" ||
    typeof node.rebootAfter !== "boolean"
  ) {
    return false;
  }

  const bodies =
    node.kind === "if"
      ? [node.then, node.else]
      : node.kind === "group" || node.kind === "repeat"
        ? [node.steps]
        : [];

  return bodies.every((body) => Array.isArray(body) && body.every(isNode));
}

// The nodes of text Ctrl+C put on the clipboard, or null for any other text. Their ids are the ones copied; a paste
// gives them new ones.
export function nodesFromClipboard(text: string): SequenceStep[] | null {
  try {
    const value: unknown = JSON.parse(text);

    if (typeof value !== "object" || value === null) {
      return null;
    }

    const { ddtFlow, nodes } = value as Partial<Record<keyof FlowClipboard, unknown>>;

    return ddtFlow === 1 && Array.isArray(nodes) && nodes.length > 0 && nodes.every(isNode)
      ? nodes
      : null;
  } catch {
    return null;
  }
}
