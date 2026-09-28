// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useNavigate, useSearch } from "@tanstack/react-router";
import { useCallback, useEffect, useRef, useState } from "react";

import type { TreeIndex } from "../../flow/flowTree";
import type { FlowReveal } from "../canvas/useCanvasReveal";
import type { InspectorTab } from "../Inspector";

// The chosen node, the inspector's tab that goes with it, and the requests that show a node in the flow.
export function useFlowSelection(index: TreeIndex) {
  const search = useSearch({ from: "/shell/deployment/sequences/$sequenceId" });
  const navigate = useNavigate({ from: "/deployment/sequences/$sequenceId" });
  // The node shown. The URL holds it too, so a reload shows the same node. But the URL is only read when the page
  // opens, because a new URL arrives a moment after the edit that chose the node, such as adding it.
  const [selectedId, setSelectedId] = useState<string | null>(search.step ?? null);
  const [tab, setTab] = useState<InspectorTab>("node");
  const [reveal, setReveal] = useState<FlowReveal | null>(null);
  const requests = useRef(0);

  // When the page opens, the node the URL names is shown in the flow.
  useEffect(() => {
    if (selectedId !== null && index.byId.has(selectedId)) {
      requests.current++;
      setReveal({ id: selectedId, focus: false, center: true, count: requests.current });
    }
    // Once, when the page opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const select = useCallback(
    (id: string | null) => {
      setSelectedId(id);
      setTab("node");
      void navigate({
        search: id === null ? {} : { step: id },
        replace: true,
        resetScroll: false,
      });
    },
    [navigate],
  );

  // Chooses a node and shows it in the flow. It gets the focus unless a field takes it.
  const show = (id: string, { focus = true, center = false } = {}) => {
    select(id);
    requests.current++;
    setReveal({ id, focus, center, count: requests.current });
  };

  return { selectedId, tab, setTab, reveal, select, show };
}

export type FlowSelection = ReturnType<typeof useFlowSelection>;
