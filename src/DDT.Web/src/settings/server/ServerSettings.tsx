// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { useNavigate, useSearch } from "@tanstack/react-router";

import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Skeleton } from "@/ui/Skeleton";
import { Tab, TabList, TabPanel, Tabs } from "@/ui/Tabs";
import { SetupChecklist } from "@/setup/SetupChecklist";

import { AgentPanel } from "../AgentPanel";
import { CertificatePanel } from "../CertificatePanel";
import { ConsolePanel } from "../ConsolePanel";
import { LogFiles } from "../logging/LogFiles";
import { LoggingPanel } from "../LoggingPanel";
import { ProxiesPanel } from "../ProxiesPanel";
import { serverSearch, type ServerTab } from "../serverSearch";
import { settingsOverviewQuery } from "../settings";

import { ConfigurationPanel } from "./ConfigurationPanel";
import { SectionsPanel } from "./SectionsPanel";

// The shown tab is in the URL, so the overview can link to a tab and a reload stays on it.
export function ServerSettings() {
  const { t } = useLingui();
  const search = serverSearch({ ...useSearch({ from: "/shell/admin/server" }) });
  const navigate = useNavigate({ from: "/admin/server" });
  const overview = useQuery(settingsOverviewQuery);
  const tab: ServerTab = search.tab ?? "overview";

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Server</Trans>} />
      <SetupChecklist />

      {overview.data?.keyRingReadable === false ? (
        <Notice tone="fail" title={<Trans>This server saves no settings</Trans>}>
          <Trans>
            This process cannot read the key ring the stored secrets were encrypted with, so it
            refuses every save. Every DDT process on one database has to share the key ring in
            DDT:StorePath/keys.
          </Trans>
        </Notice>
      ) : null}

      <Tabs
        selectedKey={tab}
        onSelectionChange={(key) => {
          const next = serverSearch({ tab: key });

          void navigate({ search: next, replace: true });
        }}
      >
        <TabList aria-label={t`Server settings`}>
          <Tab id="overview">
            <Trans>Overview</Trans>
          </Tab>
          <Tab id="certificate">
            <Trans>Certificate</Trans>
          </Tab>
          <Tab id="proxies">
            <Trans>Proxies</Trans>
          </Tab>
          <Tab id="agent">
            <Trans>Agent</Trans>
          </Tab>
          <Tab id="logging">
            <Trans>Logging</Trans>
          </Tab>
        </TabList>
        <TabPanel id="overview" className="flex flex-col gap-4">
          {overview.isError ? (
            <Notice tone="fail">
              <Trans>The server's settings could not be read.</Trans>
            </Notice>
          ) : overview.data === undefined ? (
            <Skeleton className="h-64 w-full" />
          ) : (
            <>
              <SectionsPanel sections={overview.data.sections} />
              <ConfigurationPanel settings={overview.data.server} />
            </>
          )}
        </TabPanel>
        <TabPanel id="certificate" className="flex flex-col gap-4">
          <CertificatePanel />
        </TabPanel>
        <TabPanel id="proxies" className="flex flex-col gap-4">
          <ProxiesPanel />
        </TabPanel>
        <TabPanel id="agent" className="flex flex-col gap-4">
          <AgentPanel />
          <ConsolePanel />
        </TabPanel>
        <TabPanel id="logging" className="flex flex-col gap-4">
          <LoggingPanel />
          <LogFiles />
        </TabPanel>
      </Tabs>
    </Page>
  );
}
