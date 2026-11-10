// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { BinaryPanel } from "./agent/BinaryPanel";
import {
  consoleBinaryQuery,
  maxConsoleBytes,
  removeConsoleUpload,
  uploadConsole,
} from "./agentBinary";

// The console that netbooting machines show, both in WinPE and as the shell of DDT's session in the installed
// Windows. The agent downloads it the same way it downloads itself. So a console for a newer agent protocol
// doesn't need a new boot image either.
export function ConsolePanel() {
  const { t: translate } = useLingui();

  return (
    <BinaryPanel
      binary={{
        query: consoleBinaryQuery,
        upload: uploadConsole,
        remove: removeConsoleUpload,
        maxBytes: maxConsoleBytes,
        accept: [".zip"],
        configurationKey: "DDT:Agent:ConsolePath",
        unreadable: <Trans>The console machines netboot with could not be read.</Trans>,
        title: <Trans>Console for netbooting machines</Trans>,
        runsLabel: <Trans>Machines show</Trans>,
        runs: {
          Uploaded: <Trans>The console uploaded here</Trans>,
          Bundled: <Trans>The console this server came with</Trans>,
          Configuration: <Trans>The zip DDT:Agent:ConsolePath names in configuration</Trans>,
          None: <Trans>The console in their boot image</Trans>,
        },
        hashLabel: <Trans>SHA-256 of ddt-console.exe</Trans>,
        sizeLabel: <Trans>Size of its files</Trans>,
        configured: (
          <Trans>
            DDT:Agent:ConsolePath names the console in configuration. Remove the key and restart DDT
            to upload it here.
          </Trans>
        ),
        uploadTitle: <Trans>Upload the console</Trans>,
        warning: <Trans>The console runs as SYSTEM on every machine that netboots.</Trans>,
        dropLabel: translate`Drop the console to upload it`,
        dropHere: <Trans>Drop the zip with ddt-console.exe here, or choose it.</Trans>,
        dropHint: (
          <Trans>
            A zip of ddt-console.exe, libSkiaSharp.dll and libHarfBuzzSharp.dll, of at most 128 MB.
          </Trans>
        ),
        chooseFirst: () => t`Choose the console first.`,
        uploaded: <Trans>Uploaded. Machines that netboot from now on show it.</Trans>,
        confirmTitle: (name) => <Trans>Upload {name} as the console?</Trans>,
        confirmLabel: <Trans>Upload console</Trans>,
        confirmBody: (name, size) => (
          <Trans>
            {name}, {size}, replaces the console every machine that netboots from now on runs as
            SYSTEM.
          </Trans>
        ),
        newer: (version) => (
          <Trans>
            This server came with console {version}, which is newer than the uploaded one.
          </Trans>
        ),
        removeLabel: <Trans>Remove the uploaded console</Trans>,
        removeTitle: <Trans>Remove the uploaded console?</Trans>,
        removeBody: (
          <Trans>
            Machines that netboot from now on show the console this server came with, or the one in
            their boot image.
          </Trans>
        ),
        removing: <Trans>Removing the uploaded console</Trans>,
        notRemoved: (reason) => <Trans>The uploaded console was not removed. {reason}</Trans>,
      }}
    />
  );
}
