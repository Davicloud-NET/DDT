// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { BinaryPanel } from "./agent/BinaryPanel";
import { consoleBinaryQuery, maxConsoleBytes, uploadConsole } from "./agentBinary";

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
        maxBytes: maxConsoleBytes,
        accept: [".zip"],
        configurationKey: "DDT:Agent:ConsolePath",
        unreadable: <Trans>The console machines netboot with could not be read.</Trans>,
        title: <Trans>Console for netbooting machines</Trans>,
        runsLabel: <Trans>Machines show</Trans>,
        runs: {
          Uploaded: <Trans>The console uploaded here</Trans>,
          Configuration: <Trans>The zip DDT:Agent:ConsolePath names in configuration</Trans>,
          None: <Trans>The console in their boot image, since none was uploaded</Trans>,
        },
        hashLabel: <Trans>SHA-256 of ddt-console.exe</Trans>,
        sizeLabel: <Trans>Size of its files</Trans>,
        configured: (
          <Trans>
            DDT:Agent:ConsolePath names the console in configuration, so it cannot be uploaded here.
            Remove the key and restart DDT to upload the console on this page.
          </Trans>
        ),
        uploadTitle: <Trans>Upload the console</Trans>,
        warning: (
          <Trans>
            The console you upload runs as SYSTEM in Windows PE on every machine that netboots from
            now on, and as the shell of DDT&apos;s session in the installed Windows. Upload only a
            zip of the folder Publish-Console.ps1 writes, from a DDT release or as the script builds
            it.
          </Trans>
        ),
        explanation: (
          <Trans>
            Machines take it at their next netboot: the agent downloads it and shows it instead of
            the console in the boot image, in Windows PE and in DDT&apos;s session. A machine that
            cannot download it keeps the console in its boot image.
          </Trans>
        ),
        dropLabel: translate`Drop the console to upload it`,
        dropHere: <Trans>Drop the zip with ddt-console.exe here, or choose it.</Trans>,
        dropHint: (
          <Trans>
            A zip of ddt-console.exe, libSkiaSharp.dll and libHarfBuzzSharp.dll, of at most 128 MB.
          </Trans>
        ),
        chooseFirst: () => t`Choose the console first.`,
        tooLarge: (name, limit) =>
          t`${name} is larger than ${limit}, the most the server takes for the console.`,
        uploaded: (uploaded) => (
          <Trans>
            Uploaded. Machines that netboot from now on show the console whose ddt-console.exe has
            SHA-256 {uploaded}.
          </Trans>
        ),
        confirmTitle: (name) => <Trans>Upload {name} as the console?</Trans>,
        confirmLabel: <Trans>Upload console</Trans>,
        confirmBody: (name, size) => (
          <Trans>
            {name}, {size}, replaces the console every machine that netboots from now on runs as
            SYSTEM.
          </Trans>
        ),
        reauthReason: (
          <Trans>
            The console runs as SYSTEM on every machine that netboots, so uploading it needs your
            password again.
          </Trans>
        ),
      }}
    />
  );
}
