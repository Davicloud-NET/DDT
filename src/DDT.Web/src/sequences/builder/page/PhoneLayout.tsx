// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconAdjustmentsHorizontal } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { buttonClass } from "@/ui/buttonClass";
import { Drawer } from "@/ui/Drawer";
import { Panel } from "@/ui/Panel";

import { nodeTitle } from "../../flow/flowLabels";
import { AddStepMenu } from "./AddStepMenu";
import { BuilderInspector } from "./BuilderInspector";
import { BuilderOutline } from "./BuilderOutline";
import type { FlowBuilderModel } from "./useFlowBuilder";

// The page on a phone: the outline, with the inspector in a drawer over it.
export function PhoneLayout({ model, name }: { model: FlowBuilderModel; name: string }) {
  const { selected } = model;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2">
        {model.editor.locked ? null : (
          <AddStepMenu selected={selected} onAdd={model.addAtSelection} />
        )}
        <AriaButton
          className={buttonClass("quiet", "sm")}
          onPress={() => {
            model.selection.setTab(selected === undefined ? "sequence" : "node");
            model.setDrawer(true);
          }}
        >
          <IconAdjustmentsHorizontal aria-hidden="true" size={16} stroke={2} />
          <Trans>Details</Trans>
        </AriaButton>
      </div>
      <Panel flush>
        <BuilderOutline model={model} name={name} />
      </Panel>
      <Drawer
        isOpen={model.drawer}
        onOpenChange={model.setDrawer}
        title={selected === undefined ? name : nodeTitle(selected.node)}
      >
        <BuilderInspector model={model} />
      </Drawer>
    </div>
  );
}
