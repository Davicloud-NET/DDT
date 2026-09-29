// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Facts } from "@/ui/Facts";

import type { PxeConfiguration } from "../networkBoot";
import { SettingsGroup } from "../parts/SettingsGroup";

// HttpBootPort and BootDirectory are read before the server starts, so they stay in configuration.
export function ConfigurationGroup({ configuration }: { configuration: PxeConfiguration | null }) {
  const { t } = useLingui();
  const unknown = t`Not known`;

  return (
    <SettingsGroup title={<Trans>Set in configuration</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          These stay in configuration, as DDT:Pxe:HttpBootPort and DDT:Pxe:BootDirectory: a port
          that is taken would stop the server, and everything in the boot directory is served to
          anyone who asks. A relative boot directory is inside DDT:StorePath.
        </Trans>
      </p>
      <Facts
        items={[
          {
            label: <Trans>HTTP boot port</Trans>,
            value:
              configuration?.httpBootPort === null || configuration?.httpBootPort === undefined
                ? unknown
                : String(configuration.httpBootPort),
            mono: true,
          },
          {
            label: <Trans>Boot directory</Trans>,
            value: configuration?.bootDirectory ?? unknown,
            mono: true,
          },
        ]}
      />
    </SettingsGroup>
  );
}
