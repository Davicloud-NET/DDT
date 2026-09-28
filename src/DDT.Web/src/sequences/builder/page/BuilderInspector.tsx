// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { sequenceFindings } from "../../problems";
import { Inspector } from "../Inspector";
import { ProblemsPanel } from "../ProblemsPanel";
import { VariablesPanel } from "../VariablesPanel";
import { NodeTab } from "./NodeTab";
import { SequenceTab } from "./SequenceTab";
import type { FlowBuilderModel } from "./useFlowBuilder";

// The inspector with the page's four tabs, beside the flow or in the drawer on a phone.
export function BuilderInspector({ model }: { model: FlowBuilderModel }) {
  const { editor, index, selection, inspector, edits } = model;
  const { draft } = editor;

  return (
    <Inspector
      tab={selection.tab}
      onTab={selection.setTab}
      problemCount={editor.findings.problems.length}
      warningCount={editor.findings.warnings.length}
      panelRef={inspector.ref}
      onKeyDown={inspector.onKeyDown}
      className={
        model.phone ? "-mx-5 -my-4 rounded-none bg-transparent shadow-none" : "w-92 shrink-0"
      }
      node={<NodeTab model={model} />}
      variables={
        <VariablesPanel
          draft={draft}
          findings={sequenceFindings(editor.findings, draft.steps)}
          open={inspector.openRow}
          onEdit={edits.edit}
          onGoToNode={(id) => {
            selection.show(id, { center: true });
          }}
        />
      }
      problems={<ProblemsPanel index={index} findings={editor.findings} onGoTo={inspector.goTo} />}
      sequence={<SequenceTab state={editor.state} draft={draft} onEdit={edits.edit} />}
    />
  );
}
