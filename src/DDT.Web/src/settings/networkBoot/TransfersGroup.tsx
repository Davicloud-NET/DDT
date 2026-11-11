// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { PxeForm } from "../networkBoot";
import { SettingLines } from "../parts/SettingLines";
import { SettingNumber } from "../parts/SettingNumber";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingSwitch } from "../parts/SettingSwitch";

export function TransfersGroup({ form }: { form: PxeForm }) {
  return (
    <SettingsGroup title={<Trans>ProxyDHCP and TFTP</Trans>}>
      <SettingSwitch
        form={form}
        field="enableProxyDhcp"
        canChange
        label={<Trans>Answer as ProxyDHCP</Trans>}
        hint={
          <Trans>
            Turn it off only where every DHCP server names DDT and the boot file itself.
          </Trans>
        }
      />
      <SettingSwitch
        form={form}
        field="leaveDhcpPort"
        canChange
        label={<Trans>Leave UDP 67 to a DHCP server on this computer</Trans>}
        hint={<Trans>That DHCP server then has to send option 60, PXEClient.</Trans>}
      />
      <SettingSwitch
        form={form}
        field="enableTftp"
        canChange
        label={<Trans>Serve boot files over TFTP</Trans>}
        hint={
          <Trans>
            Turn it off only when another TFTP server serves them. Every TFTP boot target then needs
            that server's address.
          </Trans>
        }
      />
      <SettingSwitch
        form={form}
        field="tftpSinglePort"
        canChange
        label={<Trans>Answer TFTP from port 69</Trans>}
        hint={<Trans>For a stateful firewall that drops replies from a fresh port.</Trans>}
      />
      <div className="flex flex-wrap gap-4">
        <SettingNumber
          form={form}
          field="tftpMaxWindowSize"
          canChange
          label={<Trans>Largest TFTP window</Trans>}
          hint={<Trans>1 to 64. Lower it to 8 or 4 where a link loses packets.</Trans>}
          minValue={1}
          maxValue={64}
        />
        <SettingNumber
          form={form}
          field="maxConcurrentTftpTransfers"
          canChange
          label={<Trans>TFTP transfers at once</Trans>}
          hint={<Trans>A machine over the limit waits until a transfer ends.</Trans>}
          minValue={1}
        />
      </div>
      <SettingLines
        form={form}
        field="authorisedRelayAgents"
        canChange
        label={<Trans>Authorized relay agents</Trans>}
        hint={<Trans>One IPv4 address per line.</Trans>}
      />
    </SettingsGroup>
  );
}
