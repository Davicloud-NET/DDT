// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Skeleton } from "@/ui/Skeleton";

import { pxeConfigurationQuery, pxeInterfacesQuery, type PxeSettings } from "../networkBoot";
import { SettingsSection } from "../parts/SettingsSection";
import { useSettingsForm } from "../useSettingsForm";

import { BootTargetsGroup } from "./BootTargetsGroup";
import { ConfigurationGroup } from "./ConfigurationGroup";
import { InterfacesGroup } from "./InterfacesGroup";
import { NeighboursGroup } from "./neighbours/NeighboursGroup";
import { SectionProblems } from "./SectionProblems";
import { TransfersGroup } from "./TransfersGroup";

// Which interfaces DDT answers netboot on, how ProxyDHCP and TFTP behave, and which boot file each client
// architecture is sent.
export function NetworkBootSettings() {
  const form = useSettingsForm<PxeSettings>("pxe");
  const live = useLiveStatus();
  const hosts = useQuery({ ...pxeInterfacesQuery, ...liveListOptions(live) });
  const configuration = useQuery(pxeConfigurationQuery).data ?? null;
  const view = form.view;
  const values = form.values;

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Network boot</Trans>} />
      {form.query.isError ? (
        <Notice tone="fail">
          <Trans>The network boot settings could not be loaded.</Trans>
        </Notice>
      ) : view === null || values === null ? (
        <Skeleton className="h-64 w-full" />
      ) : (
        <SettingsSection
          form={form}
          canChange
          title={<Trans>Answering machines that netboot</Trans>}
          description={
            <Trans>
              DDT answers netboot only on the interfaces listed here. ProxyDHCP tells each machine
              which boot file to load, and TFTP or HTTP serves it from the boot directory.
            </Trans>
          }
        >
          <SectionProblems form={form} />
          <InterfacesGroup form={form} values={values} hosts={hosts} />
          <TransfersGroup form={form} />
          <BootTargetsGroup
            form={form}
            values={values}
            hosts={hosts.data ?? []}
            configuration={configuration}
          />
          <NeighboursGroup />
          <ConfigurationGroup configuration={configuration} />
          {form.dirty ? (
            <Notice tone="attention">
              <Trans>
                Saving restarts the network boot listeners on every host that runs them, which ends
                the TFTP transfers in progress. Those machines start their download again.
              </Trans>
            </Notice>
          ) : null}
        </SettingsSection>
      )}
    </Page>
  );
}
