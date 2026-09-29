// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { FlowOutline } from "../FlowOutline";
import type { FlowBuilderModel } from "./useFlowBuilder";

// The page's outline. On a phone, choosing a row opens its fields in the drawer.
export function BuilderOutline({ model, name }: { model: FlowBuilderModel; name: string }) {
  const { t } = useLingui();
  const { editor, selection } = model;

  return (
    <FlowOutline
      label={t`Outline of ${name}`}
      steps={editor.draft.steps}
      index={model.index}
      selectedId={selection.selectedId}
      findings={editor.findings}
      locked={editor.locked}
      onSelect={(id) => {
        selection.select(id);

        if (model.phone) {
          model.setDrawer(true);
        }
      }}
      onMove={model.edits.move}
      className="min-h-0 flex-1"
    />
  );
}
