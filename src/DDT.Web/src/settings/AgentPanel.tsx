// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { BinaryPanel } from "./agent/BinaryPanel";
import { agentBinaryQuery, maxAgentBytes, removeAgentUpload, uploadAgent } from "./agentBinary";

// The agent that netbooting machines switch to. At each boot, the boot image's agent downloads it if it differs and
// runs it instead, so an upload doesn't need a new boot image. It runs as SYSTEM everywhere, so an upload needs the
// password again.
export function AgentPanel() {
  const { t: translate } = useLingui();

  return (
    <BinaryPanel
      binary={{
        query: agentBinaryQuery,
        upload: uploadAgent,
        remove: removeAgentUpload,
        maxBytes: maxAgentBytes,
        accept: [".exe"],
        configurationKey: "DDT:Agent:BinaryPath",
        unreadable: <Trans>The agent machines netboot with could not be read.</Trans>,
        title: <Trans>Agent for netbooting machines</Trans>,
        runsLabel: <Trans>Machines run</Trans>,
        runs: {
          Uploaded: <Trans>The agent uploaded here</Trans>,
          Bundled: <Trans>The agent this server came with</Trans>,
          Configuration: <Trans>The file DDT:Agent:BinaryPath names in configuration</Trans>,
          None: <Trans>The agent in their boot image</Trans>,
        },
        hashLabel: <Trans>SHA-256</Trans>,
        sizeLabel: <Trans>Size</Trans>,
        configured: (
          <Trans>
            DDT:Agent:BinaryPath names the agent in configuration. Remove the key and restart DDT to
            upload it here.
          </Trans>
        ),
        uploadTitle: <Trans>Upload the agent</Trans>,
        warning: (
          <Trans>
            The agent runs as SYSTEM on every machine that netboots, before anyone authorizes it.
          </Trans>
        ),
        dropLabel: translate`Drop the agent to upload it`,
        dropHere: <Trans>Drop ddt-agent.exe here, or choose it.</Trans>,
        dropHint: <Trans>A Windows executable of at most 128 MB.</Trans>,
        chooseFirst: () => t`Choose the agent first.`,
        uploaded: <Trans>Uploaded. Machines that netboot from now on run it.</Trans>,
        confirmTitle: (name) => <Trans>Upload {name} as the agent?</Trans>,
        confirmLabel: <Trans>Upload agent</Trans>,
        confirmBody: (name, size) => (
          <Trans>
            {name}, {size}, replaces the agent every machine that netboots from now on runs as
            SYSTEM.
          </Trans>
        ),
        newer: (version) => (
          <Trans>
            This server came with agent {version}, which is newer than the uploaded one.
          </Trans>
        ),
        removeLabel: <Trans>Remove the uploaded agent</Trans>,
        removeTitle: <Trans>Remove the uploaded agent?</Trans>,
        removeBody: (
          <Trans>
            Machines that netboot from now on run the agent this server came with, or the one in
            their boot image.
          </Trans>
        ),
        removing: <Trans>Removing the uploaded agent</Trans>,
        notRemoved: (reason) => <Trans>The uploaded agent was not removed. {reason}</Trans>,
      }}
    />
  );
}
