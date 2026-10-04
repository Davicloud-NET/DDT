// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { formattingLocale } from "@/i18n/i18n";
import { relativeTime } from "@/lib/relativeTime";
import { Facts } from "@/ui/Facts";
import { Panel } from "@/ui/Panel";

import type { BootImageView } from "./bootImage";
import { keyboardLayoutName } from "./keyboardLayouts";

// The build machines netboot: what it has in it, and what it was built for.
export function LastBuild({ view, now }: { view: BootImageView; now: number }) {
  const build = view.build;
  const unknown = t`Not recorded`;

  return (
    <Panel title={<Trans>Served build</Trans>}>
      {build === null ? (
        <p className="text-ink-2">
          <Trans>
            The boot directory holds no record of a build. A build with the current script writes
            ddt-boot-image.json next to boot.wim, which DDT reads here.
          </Trans>
        </p>
      ) : (
        <Facts
          items={[
            {
              label: <Trans>Built</Trans>,
              value: (
                <span title={new Date(build.builtUtc).toLocaleString(formattingLocale())}>
                  {relativeTime(build.builtUtc, now)}
                </span>
              ),
            },
            { label: <Trans>Drivers</Trans>, value: String(build.drivers.length) },
            { label: <Trans>ADK</Trans>, value: build.adkVersion ?? unknown },
            { label: <Trans>Boot manager</Trans>, value: build.bootManager ?? unknown },
            { label: <Trans>Agent</Trans>, value: build.agentVersion ?? unknown },
            { label: <Trans>Server address</Trans>, value: build.serverUrl ?? unknown },
            {
              label: <Trans>Keyboard layout</Trans>,
              value:
                build.keyboardLayout === null ? unknown : keyboardLayoutName(build.keyboardLayout),
            },
            {
              label: <Trans>PowerShell</Trans>,
              value:
                build.powerShell === null ? unknown : build.powerShell ? t`Included` : t`Left out`,
            },
          ]}
        />
      )}
    </Panel>
  );
}
