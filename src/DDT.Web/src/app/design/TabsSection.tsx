// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Panel } from "@/ui/Panel";
import { Tab, TabList, TabPanel, Tabs } from "@/ui/Tabs";

export function TabsSection() {
  return (
    <Panel title="Tabs">
      <Tabs>
        <TabList aria-label="Machine">
          <Tab id="run">Current run</Tab>
          <Tab id="history">Run history</Tab>
          <Tab id="log">Log</Tab>
        </TabList>
        <TabPanel id="run">The steps and the current one.</TabPanel>
        <TabPanel id="history">Earlier runs.</TabPanel>
        <TabPanel id="log">Every line the agent sent.</TabPanel>
      </Tabs>
    </Panel>
  );
}
