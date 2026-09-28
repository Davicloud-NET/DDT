// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { BinaryPanel } from "./agent/BinaryPanel";
import { agentBinaryQuery, maxAgentBytes, uploadAgent } from "./agentBinary";

// The agent netbooting machines switch to: the boot image's agent downloads it at each boot when it differs and runs
// it instead, so an upload needs no new boot image. It runs as SYSTEM everywhere, so it needs the password again.
export function AgentPanel() {
  const { t: translate } = useLingui();

  return (
    <BinaryPanel
      binary={{
        query: agentBinaryQuery,
        upload: uploadAgent,
        maxBytes: maxAgentBytes,
        accept: [".exe"],
        configurationKey: "DDT:Agent:BinaryPath",
        unreadable: <Trans>The agent machines netboot with could not be read.</Trans>,
        title: <Trans>Agent for netbooting machines</Trans>,
        runsLabel: <Trans>Machines run</Trans>,
        runs: {
          Uploaded: <Trans>The agent uploaded here</Trans>,
          Configuration: <Trans>The file DDT:Agent:BinaryPath names in configuration</Trans>,
          None: <Trans>The agent in their boot image, since none was uploaded</Trans>,
        },
        hashLabel: <Trans>SHA-256</Trans>,
        sizeLabel: <Trans>Size</Trans>,
        configured: (
          <Trans>
            DDT:Agent:BinaryPath names the agent in configuration, so it cannot be uploaded here.
            Remove the key and restart DDT to upload the agent on this page.
          </Trans>
        ),
        uploadTitle: <Trans>Upload the agent</Trans>,
        warning: (
          <Trans>
            The agent you upload runs as SYSTEM on every machine that netboots from now on, before
            anybody authorized the machine. Upload only ddt-agent.exe from a DDT release or as
            Publish-Agent.ps1 builds it.
          </Trans>
        ),
        explanation: (
          <Trans>
            Machines take it at their next netboot: the agent in the boot image downloads it and
            runs it instead, so the boot image does not have to be built again. A machine that
            cannot download or start it goes on with the agent in its boot image.
          </Trans>
        ),
        dropLabel: translate`Drop the agent to upload it`,
        dropHere: <Trans>Drop ddt-agent.exe here, or choose it.</Trans>,
        dropHint: <Trans>A Windows executable of at most 128 MB.</Trans>,
        chooseFirst: () => t`Choose the agent first.`,
        tooLarge: (name, limit) =>
          t`${name} is larger than ${limit}, the most the server takes for the agent.`,
        uploaded: (uploaded) => (
          <Trans>
            Uploaded. Machines that netboot from now on run the agent with SHA-256 {uploaded}.
          </Trans>
        ),
        confirmTitle: (name) => <Trans>Upload {name} as the agent?</Trans>,
        confirmLabel: <Trans>Upload agent</Trans>,
        confirmBody: (name, size) => (
          <Trans>
            {name}, {size}, replaces the agent every machine that netboots from now on runs as
            SYSTEM.
          </Trans>
        ),
        reauthReason: (
          <Trans>
            The agent runs as SYSTEM on every machine that netboots, so uploading it needs your
            password again.
          </Trans>
        ),
      }}
    />
  );
}
