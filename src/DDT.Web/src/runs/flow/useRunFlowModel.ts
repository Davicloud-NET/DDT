// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMemo } from "react";

import type { Subject } from "@/conditions/conditionSubjects";
import type { DeploymentSummary, DeploymentView } from "@/deployments/deployments";
import type { FlowLayout } from "@/sequences/flow/flowGeometry";
import { layoutFlow } from "@/sequences/flow/flowLayout";
import { indexTree, type TreeIndex } from "@/sequences/flow/flowTree";

import { runSubjects } from "../decisions";
import { runPath, type RunPath } from "../runPath";

export interface RunFlowModel {
  layout: FlowLayout;
  index: TreeIndex;
  subjects: Subject[];
  path: RunPath;
}

// The run's own copy of its sequence, laid out the same way as in the flow builder, and the path the run took through
// it.
export function useRunFlowModel(view: DeploymentView, summary: DeploymentSummary): RunFlowModel {
  const definition = view.definition;
  const steps = useMemo(() => definition?.steps ?? [], [definition]);
  const layout = useMemo(() => layoutFlow(steps), [steps]);
  const index = useMemo(() => indexTree(steps), [steps]);
  const subjects = useMemo(
    () => runSubjects(definition, view.values ?? []),
    [definition, view.values],
  );
  const path = runPath(definition, view.steps, {
    activity: summary.activity,
    pause: view.pause ?? null,
  });

  return { layout, index, subjects, path };
}
