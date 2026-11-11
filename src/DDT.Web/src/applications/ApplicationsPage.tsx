// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Tab, TabList, TabPanel, Tabs } from "@/ui/Tabs";

import { ApplicationsTab } from "./ApplicationsTab";
import { BundlesTab } from "./bundles/BundlesTab";

// The application library and its bundles. Applications install as SYSTEM, so only administrators change them.
export function ApplicationsPage() {
  const { t } = useLingui();

  return (
    <Page>
      <PageHeader title={<Trans>Applications</Trans>} />

      <Tabs defaultSelectedKey="applications">
        <TabList aria-label={t`Application library`}>
          <Tab id="applications">
            <Trans>Applications</Trans>
          </Tab>
          <Tab id="bundles">
            <Trans>Bundles</Trans>
          </Tab>
        </TabList>
        <TabPanel id="applications">
          <ApplicationsTab />
        </TabPanel>
        <TabPanel id="bundles">
          <BundlesTab />
        </TabPanel>
      </Tabs>
    </Page>
  );
}
