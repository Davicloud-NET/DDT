// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { BinaryPanel } from "./agent/BinaryPanel";
import {
  linuxAgentBinaryQuery,
  maxAgentBytes,
  removeLinuxAgentUpload,
  uploadLinuxAgent,
} from "./agentBinary";

// The agent that machines in the Linux boot environment switch to. The agent the environment brings downloads it if
// it differs and runs it instead. It runs as root everywhere, so an upload needs the password again.
export function LinuxAgentPanel() {
  const { t: translate } = useLingui();

  return (
    <BinaryPanel
      binary={{
        query: linuxAgentBinaryQuery,
        upload: uploadLinuxAgent,
        remove: removeLinuxAgentUpload,
        maxBytes: maxAgentBytes,
        // The file has no extension to go by.
        accept: [],
        configurationKey: "DDT:Agent:LinuxBinaryPath",
        unreadable: <Trans>The agent for the Linux boot environment could not be read.</Trans>,
        title: <Trans>Agent for the Linux boot environment</Trans>,
        runsLabel: <Trans>Machines run</Trans>,
        runs: {
          Uploaded: <Trans>The agent for Linux uploaded here</Trans>,
          Bundled: <Trans>The agent for Linux this server came with</Trans>,
          Configuration: <Trans>The file DDT:Agent:LinuxBinaryPath names in configuration</Trans>,
          None: <Trans>The agent the Linux boot environment brings</Trans>,
        },
        hashLabel: <Trans>SHA-256</Trans>,
        sizeLabel: <Trans>Size</Trans>,
        configured: (
          <Trans>
            DDT:Agent:LinuxBinaryPath names the agent for Linux in configuration. Remove the key and
            restart DDT to upload it here.
          </Trans>
        ),
        uploadTitle: <Trans>Upload the agent for Linux</Trans>,
        warning: (
          <Trans>
            The agent runs as root on every machine that starts the Linux boot environment, before
            anyone authorizes it.
          </Trans>
        ),
        dropLabel: translate`Drop the agent for Linux to upload it`,
        dropHere: <Trans>Drop ddt-agent-linux-x64 here, or choose it.</Trans>,
        dropHint: <Trans>A 64-bit Linux program for x64 of at most 128 MB.</Trans>,
        chooseFirst: () => t`Choose the agent for Linux first.`,
        uploaded: (
          <Trans>
            Uploaded. Machines that start the Linux boot environment from now on run it.
          </Trans>
        ),
        confirmTitle: (name) => <Trans>Upload {name} as the agent for Linux?</Trans>,
        confirmLabel: <Trans>Upload agent</Trans>,
        confirmBody: (name, size) => (
          <Trans>
            {name}, {size}, replaces the agent every machine that starts the Linux boot environment
            from now on runs as root.
          </Trans>
        ),
        newer: (version) => (
          <Trans>
            This server came with agent {version}, which is newer than the uploaded one.
          </Trans>
        ),
        removeLabel: <Trans>Remove the uploaded agent for Linux</Trans>,
        removeTitle: <Trans>Remove the uploaded agent for Linux?</Trans>,
        removeBody: (
          <Trans>
            Machines that start the Linux boot environment from now on run the agent for Linux this
            server came with, or the one the environment brings.
          </Trans>
        ),
        removing: <Trans>Removing the uploaded agent for Linux</Trans>,
        notRemoved: (reason) => (
          <Trans>The uploaded agent for Linux was not removed. {reason}</Trans>
        ),
      }}
    />
  );
}
