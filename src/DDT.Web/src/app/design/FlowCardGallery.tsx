// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { FlowNode, type FlowNodeState } from "@/ui/FlowNode";

// The node states a card can show, one card each.
const nodeStates: { detail: string; state?: FlowNodeState; mark?: "problem" | "warning" }[] = [
  { detail: "Edit" },
  { detail: "Choose the image to apply.", mark: "problem" },
  { detail: "A warning", mark: "warning" },
  { detail: "62 %", state: "running" },
  { detail: "Took 3 min", state: "done" },
  { detail: "Not signed for this firmware", state: "failed" },
  { detail: "Skipped", state: "skipped" },
  { detail: "Paused for 4 min", state: "paused" },
  { detail: "Not taken", state: "notTaken" },
];

export function FlowCardGallery() {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,236px)] gap-4">
      {nodeStates.map((card, index) => (
        <FlowNode
          key={index}
          kind="applyImage"
          name="Apply image"
          number={2}
          detail={card.detail}
          percent={62}
          {...(card.state === undefined ? {} : { state: card.state })}
          {...(card.mark === undefined ? {} : { mark: card.mark })}
          className="h-16"
        />
      ))}
      <FlowNode
        kind="applyImage"
        name="Apply image"
        number={2}
        detail="Selected"
        selected
        className="h-16"
      />
      <FlowNode
        kind="if"
        name="Is it a Latitude?"
        detail='Model contains "Latitude"'
        className="h-25"
      />
      <FlowNode
        kind="group"
        name="Berlin office"
        collapsed
        state="running"
        strip={[{ state: "done" }, { state: "running", percent: 50 }, { state: "waiting" }]}
        className="h-21"
      />
    </div>
  );
}
