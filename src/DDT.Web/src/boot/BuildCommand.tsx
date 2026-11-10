// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { CopyButton } from "@/ui/CopyButton";
import { Panel } from "@/ui/Panel";

import { buildCommand, type BootImageView } from "./bootImage";

// A server without the DDT Helper service cannot build, so the page gives the command for a Windows PC with the ADK.
export function BuildCommand({ view }: { view: BootImageView }) {
  const { t: translate } = useLingui();
  const drivers = view.drivers.length > 0;
  const command = buildCommand(view.builder.serverUrl, drivers);

  return (
    <Panel
      title={<Trans>Building it again</Trans>}
      actions={
        <CopyButton size="sm" text={command}>
          <Trans>Copy command</Trans>
        </CopyButton>
      }
    >
      <p className="text-ink-2">
        <Trans>
          Run this in an elevated PowerShell on a Windows PC with the Windows ADK, from a copy of
          DDT's repository. The destination has to be in the boot directory.
        </Trans>
      </p>
      <pre
        aria-label={translate`Build command`}
        className="overflow-x-auto rounded-key bg-console px-3.5 py-3 type-data whitespace-pre-wrap text-console-text"
      >
        {command}
      </pre>
      <ul className="flex list-disc flex-col gap-1 pl-5 type-small text-ink-2">
        <li>
          <Trans>Another server name works only if the server's certificate has it.</Trans>
        </li>
        <li>
          <Trans>
            <a href="/api/about/root-certificate" className="underline">
              Download the root certificate
            </a>{" "}
            as ddt-root.pem.
          </Trans>
        </li>
        {drivers ? (
          <li>
            <Trans>
              For the flagged drivers, put an administrator's API token into DDT_API_TOKEN.
            </Trans>
          </li>
        ) : null}
      </ul>
    </Panel>
  );
}
