// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { CopyButton } from "@/ui/CopyButton";
import { Panel } from "@/ui/Panel";

import { buildCommand, type BootImageView } from "./bootImage";

// The build runs on a Windows machine with the ADK, so the page gives the command rather than building the image.
export function BuildCommand({ view }: { view: BootImageView }) {
  const { t: translate } = useLingui();
  const drivers = view.drivers.length > 0;
  const command = buildCommand(window.location.origin, drivers);

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
          Run this in an elevated PowerShell on a Windows machine with the Windows ADK, from a copy
          of DDT's repository. It writes boot.wim and the boot files into the destination, which the
          boot directory has to hold; machines netboot with the new image at once.
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
          <Trans>
            The server address is this page's. Use the name machines reach the server by, if it is
            another.
          </Trans>
        </li>
        <li>
          <Trans>
            <a href="/api/about/root-certificate" className="underline">
              Download the root certificate
            </a>{" "}
            as ddt-root.pem. The agent trusts only the server certificates it signed.
          </Trans>
        </li>
        {drivers ? (
          <li>
            <Trans>
              The script downloads the flagged drivers with an API token of an administrator. Make
              one under Account and security, and put it into DDT_API_TOKEN.
            </Trans>
          </li>
        ) : null}
      </ul>
    </Panel>
  );
}
