// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { BinaryPanel } from "./agent/BinaryPanel";
import {
  linuxConsoleBinaryQuery,
  maxConsoleBytes,
  removeLinuxConsoleUpload,
  uploadLinuxConsole,
} from "./agentBinary";

// The console the agents for Linux show: in the Linux boot environment as root, in the installed Linux as a user of
// its own. They download it as the console for Windows is downloaded.
export function LinuxConsolePanel() {
  const { t: translate } = useLingui();

  return (
    <BinaryPanel
      binary={{
        query: linuxConsoleBinaryQuery,
        upload: uploadLinuxConsole,
        remove: removeLinuxConsoleUpload,
        maxBytes: maxConsoleBytes,
        accept: [".zip"],
        configurationKey: "DDT:Agent:LinuxConsolePath",
        unreadable: <Trans>The console for Linux could not be read.</Trans>,
        title: <Trans>Console for Linux</Trans>,
        runsLabel: <Trans>Machines show</Trans>,
        runs: {
          Uploaded: <Trans>The console for Linux uploaded here</Trans>,
          Bundled: (
            <Trans>The console for Linux this server came with, since none was uploaded</Trans>
          ),
          Configuration: <Trans>The zip DDT:Agent:LinuxConsolePath names in configuration</Trans>,
          None: (
            <Trans>
              The console the Linux boot environment brings, and none in the installed Linux
            </Trans>
          ),
        },
        hashLabel: <Trans>SHA-256 of ddt-console</Trans>,
        sizeLabel: <Trans>Size of its files</Trans>,
        configured: (
          <Trans>
            DDT:Agent:LinuxConsolePath names the console for Linux in configuration, so it cannot be
            uploaded here. Remove the key and restart DDT to upload it on this page.
          </Trans>
        ),
        uploadTitle: <Trans>Upload the console for Linux</Trans>,
        warning: (
          <Trans>
            The console you upload runs as root in the Linux boot environment on every machine that
            starts it from now on. Upload only ddt-console-linux-x64.zip from a DDT release, or the
            zip Publish-LinuxConsole.ps1 writes.
          </Trans>
        ),
        explanation: (
          <Trans>
            Machines take it at the next start of their agent for Linux. One that cannot download or
            start it shows its text console.
          </Trans>
        ),
        dropLabel: translate`Drop the console for Linux to upload it`,
        dropHere: <Trans>Drop ddt-console-linux-x64.zip here, or choose it.</Trans>,
        dropHint: <Trans>A zip of the console's three files, at most 128 MB.</Trans>,
        chooseFirst: () => t`Choose the console for Linux first.`,
        tooLarge: (name, limit) =>
          t`${name} is larger than ${limit}, the most the server takes for the console.`,
        uploaded: (uploaded) => (
          <Trans>Uploaded. Machines show the console with ddt-console of SHA-256 {uploaded}.</Trans>
        ),
        confirmTitle: (name) => <Trans>Upload {name} as the console for Linux?</Trans>,
        confirmLabel: <Trans>Upload console</Trans>,
        confirmBody: (name, size) => (
          <Trans>
            {name}, {size}, replaces the console every machine with the agent for Linux shows from
            now on.
          </Trans>
        ),
        reauthReason: (
          <Trans>
            The console runs as root in the Linux boot environment, so uploading it needs your
            password again.
          </Trans>
        ),
        newer: (version) => (
          <Trans>
            This server came with console {version}, which is newer than the uploaded one. Remove
            the upload, and machines show the server's own.
          </Trans>
        ),
        removeLabel: <Trans>Remove the uploaded console for Linux</Trans>,
        removeTitle: <Trans>Remove the uploaded console for Linux?</Trans>,
        removeBody: (
          <Trans>
            Machines show the console for Linux this server came with. If it came with none, the
            Linux boot environment shows its own and the installed Linux none.
          </Trans>
        ),
        removing: <Trans>Removing the uploaded console for Linux</Trans>,
        removed: <Trans>Removed. Machines no longer get that console.</Trans>,
        notRemoved: (reason) => (
          <Trans>The uploaded console for Linux was not removed. {reason}</Trans>
        ),
      }}
    />
  );
}
