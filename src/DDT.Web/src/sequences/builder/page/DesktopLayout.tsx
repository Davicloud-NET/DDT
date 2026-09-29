// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Panel } from "@/ui/Panel";

import { stepFindings } from "../../problems";
import { FlowCanvas } from "../FlowCanvas";
import { nodeDetail } from "../nodeDetail";
import { Palette } from "../Palette";
import { AddStepMenu } from "./AddStepMenu";
import { BuilderInspector } from "./BuilderInspector";
import { BuilderOutline } from "./BuilderOutline";
import type { FlowBuilderModel } from "./useFlowBuilder";

// The page on a wider screen: the palette, the flow or its outline, and the inspector.
export function DesktopLayout({ model, name }: { model: FlowBuilderModel; name: string }) {
  const { t } = useLingui();
  const { editor, selection, data, edits } = model;
  const { locked } = editor;

  return (
    <div className="flex min-h-[36rem] flex-1 gap-4">
      {locked || model.view === "outline" ? null : (
        <Palette
          className="hidden w-54 shrink-0 xl:flex"
          onDragChange={model.setDrag}
          onAdd={model.addAtSelection}
        />
      )}
      {model.view === "flow" ? (
        <FlowCanvas
          label={t`Flow of ${name}`}
          steps={editor.draft.steps}
          index={model.index}
          layout={model.layout}
          selectedId={selection.selectedId}
          reveal={selection.reveal}
          findings={editor.findings}
          phases={editor.phases}
          collapsed={model.collapsed}
          locked={locked}
          drag={model.drag}
          onDragChange={model.setDrag}
          detailOf={(node) =>
            nodeDetail(node, {
              catalog: editor.catalog,
              subjects: data.subjects,
              findings: stepFindings(editor.findings, node.id),
              accounts: data.accounts,
            })
          }
          onSelect={selection.select}
          onCommand={model.command}
          onAction={model.action}
          onAdd={edits.add}
          onMove={edits.move}
          addAfter={model.addAfter}
          onAddAfterDone={() => {
            model.setAddAfter(null);
          }}
          className="min-h-0 min-w-0 flex-1"
        />
      ) : (
        <Panel
          flush
          title={<Trans>Outline</Trans>}
          actions={
            locked ? null : <AddStepMenu selected={model.selected} onAdd={model.addAtSelection} />
          }
          className="min-h-0 min-w-0 flex-1"
        >
          <BuilderOutline model={model} name={name} />
        </Panel>
      )}
      <BuilderInspector model={model} />
    </div>
  );
}
