// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { DeploymentStepView } from "@/deployments/deployments";

import type { PathNode, RunPath } from "../runPath";

export type ShownNode = PathNode & { step: DeploymentStepView };

// The nodes the list of steps shows: those on the path that the server reported, in the order of the tree.
export function shownNodes(path: RunPath): ShownNode[] {
  return path.nodes.filter(
    (each): each is ShownNode => each.state !== "notTaken" && each.step !== null,
  );
}
