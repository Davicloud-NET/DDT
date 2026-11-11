// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { saveFile } from "@/lib/saveFile";
import { ReauthDialog } from "@/settings/parts/ReauthDialog";
import { useGuardedAction } from "@/settings/useGuardedAction";
import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { ProgressBar } from "@/ui/ProgressBar";

import { builderFileName, downloadBuilder } from "./bootImage";

// The builder: a zip that builds this server's boot image on any Windows PC with the ADK and uploads it. For a Linux
// server, and for a Windows server without the ADK.
export function BuilderPanel() {
  const administrator = useIsAdministrator();
  const [saved, setSaved] = useState(false);
  const download = useGuardedAction({
    send: () => downloadBuilder(),
    onDone: (zip) => {
      saveFile(zip, builderFileName);
      setSaved(true);
    },
    askFirst: true,
  });
  const reason = download.error;

  return (
    <Panel title={<Trans>Build on another PC</Trans>}>
      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          Unpack the builder on a Windows PC with the Windows ADK and its Windows PE add-on, then
          run Build.cmd. It uploads one boot image, within a day.
        </Trans>
      </p>

      {download.busy ? (
        <ProgressBar label={<Trans>Putting the builder together</Trans>} />
      ) : administrator ? (
        <div>
          <Button
            onPress={() => {
              setSaved(false);
              download.start();
            }}
          >
            <Trans>Download the builder</Trans>
          </Button>
        </div>
      ) : (
        <p className="type-small text-muted">
          <Trans>Only administrators download the builder.</Trans>
        </p>
      )}
      {reason === null ? null : <Notice tone="fail">{reason}</Notice>}
      {saved ? (
        <Notice>
          <Trans>Saved as {builderFileName}.</Trans>
        </Notice>
      ) : null}

      <ReauthDialog
        isOpen={download.needsReauth}
        onAccepted={download.retryAfterReauth}
        onCancel={download.cancelReauth}
        confirmLabel={<Trans>Confirm and download</Trans>}
        reason={
          <Trans>
            The boot image runs as SYSTEM on every machine that netboots. Confirm with your
            password.
          </Trans>
        }
      />
    </Panel>
  );
}
