// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMemo, useState } from "react";

import { useMediaQuery } from "@/lib/useMediaQuery";

import { slotAfter } from "../../flow/flowKeyboard";
import { layoutFlow } from "../../flow/flowLayout";
import { indexTree } from "../../flow/flowTree";
import type { SequenceView, StepKind } from "../../sequences";
import { useSequenceEditor } from "../../useSequenceEditor";
import { useBuilderData } from "../useBuilderData";
import type { FlowDrag } from "../flowDrag";
import { commandHandlers } from "./commandHandlers";
import { editHandlers } from "./editHandlers";
import { useCollapsed } from "./useCollapsed";
import { useFlowClipboard } from "./useFlowClipboard";
import { useFlowSelection } from "./useFlowSelection";
import { useInspector } from "./useInspector";

// The flow builder page's state: the sequence being edited, its tree and layout, the chosen node, and the edits.
export function useFlowBuilder(initial: SequenceView, readOnly: boolean) {
  const editor = useSequenceEditor(initial, readOnly);
  const phone = useMediaQuery("(max-width: 767px)");
  const { draft } = editor;
  const data = useBuilderData(draft);
  const index = useMemo(() => indexTree(draft.steps), [draft.steps]);
  const [collapsed, toggleCollapsed] = useCollapsed(initial.id);
  const layout = useMemo(() => layoutFlow(draft.steps, { collapsed }), [draft.steps, collapsed]);
  const selection = useFlowSelection(index);
  const [view, setView] = useState<"flow" | "outline">("flow");
  const [drag, setDrag] = useState<FlowDrag>(null);
  const [addAfter, setAddAfter] = useState<string | null>(null);
  const [drawer, setDrawer] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const openDrawer = () => {
    setDrawer(true);
  };
  const inspector = useInspector({ index, selection, view, phone, openDrawer });
  const edits = editHandlers({ editor, index, selection, announce: setAnnouncement });
  const clipboard = useFlowClipboard(index, edits.edit, selection.show, setAnnouncement);
  const { command, action } = commandHandlers({
    index,
    phone,
    selection,
    edits,
    clipboard,
    announce: setAnnouncement,
    openDrawer,
    focusWhenShown: inspector.focusWhenShown,
    toggleCollapsed,
    addAfter: setAddAfter,
  });
  const { selectedId } = selection;
  const selected = selectedId === null ? undefined : index.byId.get(selectedId);

  return {
    editor,
    data,
    phone,
    index,
    layout,
    collapsed,
    selection,
    selected,
    // The palette and the outline's button add after the chosen node, or at the end.
    addAtSelection: (kind: StepKind) => {
      edits.add(slotAfter(index, selected?.node.id ?? null), kind);
    },
    view,
    setView,
    drag,
    setDrag,
    addAfter,
    setAddAfter,
    drawer,
    setDrawer,
    announcement,
    inspector,
    edits,
    command,
    action,
  };
}

export type FlowBuilderModel = ReturnType<typeof useFlowBuilder>;
