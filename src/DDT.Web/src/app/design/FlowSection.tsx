// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { Button } from "@/ui/Button";
import { Panel } from "@/ui/Panel";
import { Switch } from "@/ui/Switch";

import { FlowCardGallery } from "./FlowCardGallery";
import { FlowDemoCanvas } from "./FlowDemoCanvas";
import { useDemoFlow } from "./useDemoFlow";

export function FlowSection() {
  const [run, setRun] = useState(false);
  const [collapsed, setCollapsed] = useState(false);
  const [selected, setSelected] = useState<string | null>("1");
  const flow = useDemoFlow(run, collapsed);

  return (
    <Panel
      title="Flow"
      actions={
        <div className="flex flex-wrap gap-4">
          <Switch isSelected={run} onChange={setRun}>
            A run
          </Switch>
          <Switch isSelected={collapsed} onChange={setCollapsed}>
            Collapse the group
          </Switch>
        </div>
      }
    >
      <div className="flex flex-col gap-4">
        <FlowDemoCanvas flow={flow} run={run} selected={selected} />
        <div className="flex flex-wrap gap-2">
          {["0", "1", "4"].map((id) => (
            <Button
              key={id}
              size="sm"
              variant="quiet"
              onPress={() => {
                setSelected(id);
              }}
            >
              Select {flow.byId.get(id)?.name}
            </Button>
          ))}
        </div>
        <FlowCardGallery />
      </div>
    </Panel>
  );
}
