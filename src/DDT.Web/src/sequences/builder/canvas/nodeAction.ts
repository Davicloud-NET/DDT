// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { FlowCommand } from "../../flow/flowKeyboard";
import type { ContainerKind } from "../../sequences";

// The things a node's menu does, beside the keys.
export type NodeAction =
  | { type: "wrap"; kind: ContainerKind }
  | { type: "unwrap" }
  | { type: "collapse" }
  | { type: "addAfter" }
  | { type: "command"; command: FlowCommand };

// The action of an item of the node's menu, by its id.
export function nodeActionOf(key: string): NodeAction | null {
  switch (key) {
    case "wrapGroup":
      return { type: "wrap", kind: "group" };
    case "wrapIf":
      return { type: "wrap", kind: "if" };
    case "wrapRepeat":
      return { type: "wrap", kind: "repeat" };
    case "unwrap":
      return { type: "unwrap" };
    case "collapse":
      return { type: "collapse" };
    case "addAfter":
      return { type: "addAfter" };
    case "duplicate":
      return { type: "command", command: { type: "duplicate" } };
    case "copy":
      return { type: "command", command: { type: "copy" } };
    case "cut":
      return { type: "command", command: { type: "cut" } };
    case "remove":
      return { type: "command", command: { type: "remove" } };
    default:
      return null;
  }
}
