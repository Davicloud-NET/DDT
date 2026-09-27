// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";

import { SettingLines, SettingsSection } from "./SettingsParts";
import { useSettingsForm } from "./useSettingsForm";

export interface ProxySettings {
  knownProxies: string[];
  knownNetworks: string[];
}

// The reverse proxies whose word DDT takes for the client they forward for. Everything that goes by a client's address,
// such as the rate limits, the audit log and zero touch, depends on it, so a change needs the password again.
export function ProxiesPanel() {
  const form = useSettingsForm<ProxySettings>("proxies");

  if (form.view === null) {
    return form.query.isError ? (
      <Notice tone="fail">
        <Trans>These settings could not be loaded.</Trans>
      </Notice>
    ) : (
      <Skeleton className="h-64 w-full" />
    );
  }

  return (
    <SettingsSection
      form={form}
      canChange
      title={<Trans>Reverse proxies</Trans>}
      description={
        <Trans>
          A reverse proxy in front of DDT tells it which client it forwards for, in X-Forwarded-For
          and X-Forwarded-Proto. Only requests from the addresses listed here may say so; from
          anywhere else DDT goes by the address the request came from. Sign-in limits, the audit log
          and zero touch networks all use that address.
        </Trans>
      }
    >
      <div className="grid gap-4 md:grid-cols-2">
        <SettingLines
          form={form}
          field="knownProxies"
          canChange
          label={<Trans>Proxy addresses</Trans>}
          hint={<Trans>One IP address per line, such as 10.0.0.2 or fd00::2.</Trans>}
        />
        <SettingLines
          form={form}
          field="knownNetworks"
          canChange
          label={<Trans>Proxy networks</Trans>}
          hint={
            <Trans>
              One network per line, such as 10.0.1.0/24, for proxies that share an address pool.
            </Trans>
          }
        />
      </div>
    </SettingsSection>
  );
}
