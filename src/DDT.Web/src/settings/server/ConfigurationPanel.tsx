// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Panel } from "@/ui/Panel";

import { ConfigurationOrigin } from "../ConfigurationOrigin";
import type { ServerSetting } from "../settings";

// What configuration alone decides, read-only: the server needs it before it can serve the page.
export function ConfigurationPanel({ settings }: { settings: ServerSetting[] }) {
  const { t } = useLingui();

  return (
    <Panel title={<Trans>Set in configuration</Trans>}>
      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          The server needs these before it can serve this page, so they stay in its configuration:
          environment variables, appsettings.json or the command line. It reads them as it starts,
          so a change there takes a restart of DDT. Values that could hold a password are never
          shown.
        </Trans>
      </p>
      <ul aria-label={t`Configuration values`} className="flex flex-col">
        {settings.map((setting) => (
          <li
            key={setting.key}
            className="grid gap-x-4 gap-y-0.5 border-t border-line-soft py-2 first:border-t-0 md:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]"
          >
            <span className="type-data break-all text-ink">{setting.key}</span>
            <span className="flex flex-wrap items-baseline gap-x-2">
              <ConfiguredValue setting={setting} />
            </span>
          </li>
        ))}
      </ul>
    </Panel>
  );
}

function ConfiguredValue({ setting }: { setting: ServerSetting }) {
  const hasOrigin = setting.isSet ? setting.source !== null : setting.value !== null;

  return (
    <>
      {setting.value !== null ? (
        <span className="type-data break-all text-ink">{setting.value}</span>
      ) : setting.isSet ? (
        <span className="text-ink">
          {setting.secret ? <Trans>Set, not shown</Trans> : <Trans>Set</Trans>}
        </span>
      ) : (
        <span className="text-muted">
          <Trans>Not set</Trans>
        </span>
      )}
      {hasOrigin ? (
        <span className="type-small text-muted">
          <ConfigurationOrigin setting={setting} />
        </span>
      ) : null}
    </>
  );
}
