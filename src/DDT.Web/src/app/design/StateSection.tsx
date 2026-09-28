// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { DeviceGlyph, type DeviceKind } from "@/ui/DeviceGlyph";
import { FilterSelector } from "@/ui/FilterSelector";
import { StateTag } from "@/ui/StateTag";

import { DesignSection } from "./DesignSection";

export function StateSection() {
  const [filter, setFilter] = useState("all");

  return (
    <DesignSection title="State">
      <div className="flex flex-wrap gap-2">
        <StateTag tone="run">Deploying</StateTag>
        <StateTag tone="attention">Waiting</StateTag>
        <StateTag tone="fail">Failed</StateTag>
        <StateTag tone="ok">Done</StateTag>
        <StateTag tone="idle">Ready</StateTag>
        <StateTag tone="retired">Retired</StateTag>
      </div>
      <FilterSelector
        label="Show machines by state"
        selected={filter}
        onChange={setFilter}
        options={[
          { id: "all", label: "All", count: 38 },
          { id: "running", label: "Deploying", count: 3 },
          { id: "waiting", label: "Waiting", count: 2, tone: "attention" },
          { id: "failed", label: "Failed", count: 1, tone: "fail" },
          { id: "done", label: "Done", count: 26 },
        ]}
      />
      <div className="flex gap-2">
        {(["laptop", "desktop", "tablet", "server", "virtual", "unknown"] as DeviceKind[]).map(
          (kind) => (
            <DeviceGlyph key={kind} kind={kind} />
          ),
        )}
      </div>
    </DesignSection>
  );
}
