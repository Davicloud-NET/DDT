// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import type { KeyboardEvent, ReactNode, Ref } from "react";

import { cx } from "@/ui/cx";
import { Tab, TabList, TabPanel, Tabs } from "@/ui/Tabs";

export type InspectorTab = "node" | "variables" | "problems" | "sequence";

interface InspectorProps {
  tab: InspectorTab;
  onTab: (tab: InspectorTab) => void;
  problemCount: number;
  warningCount: number;
  node: ReactNode;
  variables: ReactNode;
  problems: ReactNode;
  sequence: ReactNode;
  panelRef?: Ref<HTMLDivElement>;
  onKeyDown?: (event: KeyboardEvent<HTMLDivElement>) => void;
  className?: string;
}

// The panel beside the flow. It has one tab each for the chosen node, the sequence's variables and inputs, all
// findings, and the sequence's own name and description.
export function Inspector({
  tab,
  onTab,
  problemCount,
  warningCount,
  node,
  variables,
  problems,
  sequence,
  panelRef,
  onKeyDown,
  className,
}: InspectorProps) {
  const { t } = useLingui();
  const panel = "min-h-0 flex-1 overflow-y-auto px-4 pt-4 pb-6";

  return (
    <aside
      aria-label={t`Inspector`}
      className={cx("flex min-h-0 flex-col rounded-panel bg-panel shadow-panel", className)}
    >
      <Tabs
        selectedKey={tab}
        onSelectionChange={(key) => {
          onTab(String(key) as InspectorTab);
        }}
        className="flex min-h-0 flex-1 flex-col gap-0"
      >
        <TabList aria-label={t`Inspector`} className="gap-5 border-line-soft px-4">
          <Tab id="node">
            <Trans>Node</Trans>
          </Tab>
          <Tab id="variables">
            <Trans>Variables</Trans>
          </Tab>
          <Tab id="problems">
            <span className="flex items-center gap-1.5">
              <Trans>Problems</Trans>
              {problemCount + warningCount > 0 ? (
                <span
                  className={cx(
                    "inline-flex h-4.5 min-w-4.5 items-center justify-center rounded-tag px-1 type-tag",
                    problemCount > 0 ? "bg-fail text-on-fail" : "bg-attention text-on-attention",
                  )}
                >
                  {problemCount > 0 ? problemCount : warningCount}
                </span>
              ) : null}
            </span>
          </Tab>
          <Tab id="sequence">
            <Trans>Sequence</Trans>
          </Tab>
        </TabList>
        <div ref={panelRef} onKeyDown={onKeyDown} className="flex min-h-0 flex-1 flex-col">
          <TabPanel id="node" className={panel}>
            {node}
          </TabPanel>
          <TabPanel id="variables" className={panel}>
            {variables}
          </TabPanel>
          <TabPanel id="problems" className={panel}>
            {problems}
          </TabPanel>
          <TabPanel id="sequence" className={cx(panel, "flex flex-col gap-4")}>
            {sequence}
          </TabPanel>
        </div>
      </Tabs>
    </aside>
  );
}
