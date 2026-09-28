// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { SequenceStep } from "../sequences";
import { findingCounts } from "../sequenceList";
import { isContainer, stepKindLabel } from "../steps";
import type { Slot, TreeEntry, TreeIndex } from "./flowTree";

// A node's name as the flow shows it: containers say their kind first.
export function nodeTitle(node: SequenceStep): string {
  const name = node.name.trim() === "" ? t`Unnamed step` : node.name;

  switch (node.kind) {
    case "if":
      return t`If: ${name}`;
    case "group":
      return t`Group: ${name}`;
    case "repeat":
      return t`Repeat: ${name}`;
    default:
      return name;
  }
}

// Where a node sits, such as "Step 2 of Then of 'If: Is it a Latitude?'" or, at the top, "Step 3".
export function placeLabel(index: TreeIndex, entry: TreeEntry): string {
  const position = entry.index + 1;
  const container = entry.parent === null ? undefined : index.byId.get(entry.parent)?.node;

  if (container === undefined) {
    return t`Step ${position}`;
  }

  const title = nodeTitle(container);

  switch (entry.body) {
    case "then":
      return t`Step ${position} of Then of ''${title}''`;
    case "else":
      return t`Step ${position} of Else of ''${title}''`;
    default:
      return t`Step ${position} of ''${title}''`;
  }
}

// A node as a screen reader says it: where it is, its name, its kind where the name is not that, and its findings,
// such as "Step 2 of Then of 'If: Is it a Latitude?', Apply image, 1 problem".
export function nodeLabel(
  index: TreeIndex,
  id: string,
  problems: number,
  warnings: number,
): string {
  const entry = index.byId.get(id);

  if (entry === undefined) {
    return "";
  }

  const title = nodeTitle(entry.node);
  const kind = stepKindLabel(entry.node.kind);

  return [
    placeLabel(index, entry),
    title,
    isContainer(entry.node) || title === kind ? null : kind,
    findingCounts(problems, warnings),
  ]
    .filter((part) => part !== null)
    .join(", ");
}

// A gap as the key that adds there says it.
export function slotLabel(index: TreeIndex, slot: Slot): string {
  const list = index.entries.filter(
    (entry) => entry.parent === slot.parent && entry.body === slot.body,
  );
  const next = list[slot.index]?.node;
  const previous = list[slot.index - 1]?.node;

  if (previous !== undefined && next !== undefined) {
    const before = nodeTitle(previous);
    const following = nodeTitle(next);

    return t`Add a step between ${before} and ${following}`;
  }

  if (next !== undefined) {
    const name = nodeTitle(next);

    return t`Add a step before ${name}`;
  }

  if (previous !== undefined) {
    const name = nodeTitle(previous);

    return t`Add a step after ${name}`;
  }

  const container = slot.parent === null ? undefined : index.byId.get(slot.parent)?.node;

  if (container === undefined) {
    return t`Add the first step`;
  }

  const title = nodeTitle(container);

  switch (slot.body) {
    case "then":
      return t`Add a step to Then of ''${title}''`;
    case "else":
      return t`Add a step to Else of ''${title}''`;
    default:
      return t`Add a step to ''${title}''`;
  }
}
