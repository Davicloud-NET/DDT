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

export function LastBuild({ view, now }: { view: BootImageView; now: number }) {
  const build = view.build;
  const unknown = t`Not recorded`;

  return (
    <Panel title={<Trans>Last build</Trans>}>
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
          ]}
        />
      )}
    </Panel>
  );
}
