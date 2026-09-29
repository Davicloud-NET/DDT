// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useRef, useState, type KeyboardEvent } from "react";

import { focusField } from "../../editorFocus";
import type { TreeIndex } from "../../flow/flowTree";
import { declarationPlace } from "../../problems";
import type { OpenRow } from "../VariablesPanel";
import type { FlowSelection } from "./useFlowSelection";

interface InspectorInput {
  index: TreeIndex;
  selection: FlowSelection;
  view: "flow" | "outline";
  phone: boolean;
  openDrawer: () => void;
}

// The inspector beside the flow. It handles where a finding sends the focus, and Escape from a node's fields back to
// the node.
export function useInspector({ index, selection, view, phone, openDrawer }: InspectorInput) {
  const { selectedId, tab, setTab, show } = selection;
  const [openRow, setOpenRow] = useState<OpenRow | null>(null);
  const ref = useRef<HTMLDivElement>(null);
  // The field a finding sends the focus to once it shows, and how many renders it has waited so far.
  const pendingFocus = useRef<{ field: string; waited: number } | null>(null);

  // Another node's fields start scrolled to the top. This runs before the focus from a finding, which scrolls to its
  // field.
  useEffect(() => {
    const panel = ref.current?.querySelector<HTMLElement>('[role="tabpanel"]');

    if (panel !== null && panel !== undefined) {
      panel.scrollTop = 0;
    }
  }, [selectedId]);

  useEffect(() => {
    const pending = pendingFocus.current;

    if (pending === null) {
      return;
    }

    if (pending.field !== "" && focusField(ref.current, pending.field)) {
      pendingFocus.current = null;
      return;
    }

    const list = ref.current?.querySelector<HTMLElement>("[data-findings]");

    if (list !== null && list !== undefined && tab === "node") {
      list.focus();
      pendingFocus.current = null;
    } else if (pending.waited > 2) {
      pendingFocus.current = null;
    } else {
      pending.waited++;
    }
  });

  // Sends the focus to a field once it shows. "" sends it to the node's findings.
  const focusWhenShown = (field: string) => {
    pendingFocus.current = { field, waited: 0 };
  };

  // A finding moves the focus to its field. A node's finding goes to the node's fields, and the sequence's own go to
  // its tab.
  const goTo = (stepId: string | null, field: string | null) => {
    if (stepId !== null && index.byId.has(stepId)) {
      focusWhenShown(field ?? "");
      show(stepId, { focus: false, center: true });

      if (phone) {
        openDrawer();
      }

      return;
    }

    const place = declarationPlace(field);

    if (place !== null && field !== null) {
      setOpenRow({ list: place.list, index: place.index });
      setTab("variables");
      focusWhenShown(field);
    } else if (field === "name" || field === "description") {
      setTab("sequence");
      focusWhenShown(field);
    }
  };

  // Escape in the node's fields goes back to the node in the flow.
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (
      event.key === "Escape" &&
      !event.defaultPrevented &&
      selectedId !== null &&
      tab === "node" &&
      view === "flow" &&
      !phone
    ) {
      event.preventDefault();
      show(selectedId);
    }
  };

  return { ref, openRow, focusWhenShown, goTo, onKeyDown };
}
