// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { EditorLock } from "../editorLock";
import type { SequenceView } from "../sequences";
import { BuilderContext } from "./builderData";
import { BuilderNotices } from "./page/BuilderNotices";
import { DesktopLayout } from "./page/DesktopLayout";
import { FlowHeader } from "./page/FlowHeader";
import { LeaveDialog } from "./page/LeaveDialog";
import { PhoneLayout } from "./page/PhoneLayout";
import { useFlowBuilder } from "./page/useFlowBuilder";

interface FlowBuilderProps {
  initial: SequenceView;
  readOnly: boolean;
}

// Edits a sequence as a flow, or as its outline on a phone. Every change is saved as it is made, and other
// administrators' saves appear while this page has nothing unsaved.
export function FlowBuilder({ initial, readOnly }: FlowBuilderProps) {
  const { t } = useLingui();
  const model = useFlowBuilder(initial, readOnly);
  const { editor } = model;
  const name = editor.draft.name.trim() === "" ? t`Unnamed sequence` : editor.draft.name;

  return (
    <BuilderContext value={model.data}>
      <EditorLock value={editor.locked}>
        <FlowHeader
          name={name}
          editor={editor}
          readOnly={readOnly}
          phone={model.phone}
          view={model.view}
          onView={model.setView}
        />

        <BuilderNotices editor={editor} readOnly={readOnly} />

        {model.phone ? (
          <PhoneLayout model={model} name={name} />
        ) : (
          <DesktopLayout model={model} name={name} />
        )}

        <p role="status" className="sr-only">
          {model.announcement}
        </p>

        <LeaveDialog editor={editor} />
      </EditorLock>
    </BuilderContext>
  );
}
