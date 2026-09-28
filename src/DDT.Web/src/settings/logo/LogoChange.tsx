// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { FileDropZone } from "@/ui/FileDropZone";
import { Notice } from "@/ui/Notice";
import { ProgressBar } from "@/ui/ProgressBar";

import type { ConsoleLogoView } from "../consoleLogo";

import { RemoveLogoDialog } from "./RemoveLogoDialog";
import { useLogoChange } from "./useLogoChange";

export function LogoChange({ view }: { view: ConsoleLogoView }) {
  const { t: translate } = useLingui();
  const change = useLogoChange();
  const upload = change.upload;
  const name = change.name;
  const reason = upload.error ?? change.removal.error;

  return (
    <>
      {change.busy ? (
        <ProgressBar
          label={upload.busy ? <Trans>Uploading {name}</Trans> : <Trans>Removing the logo</Trans>}
        />
      ) : (
        <div className="flex flex-wrap items-center gap-3">
          <FileDropZone
            label={translate`Drop the logo to upload it`}
            title={
              view.sha256 === null ? (
                <Trans>Drop the logo here, or choose it.</Trans>
              ) : (
                <Trans>Drop another logo here to replace it, or choose one.</Trans>
              )
            }
            hint={<Trans>A PNG of at most 512 KB and 2048 by 2048 pixels.</Trans>}
            accept={["image/png"]}
            onFile={change.pick}
            className="min-w-0 flex-1"
          />
          {view.sha256 === null ? null : (
            <Button variant="quiet" onPress={change.askRemoval}>
              <Trans>Remove the logo</Trans>
            </Button>
          )}
        </div>
      )}

      {change.problem !== null ? <Notice tone="fail">{change.problem}</Notice> : null}
      {reason !== null ? (
        <Notice tone="fail">
          {upload.error !== null ? (
            <Trans>
              {name} was not uploaded. {reason}
            </Trans>
          ) : (
            <Trans>The logo was not removed. {reason}</Trans>
          )}
        </Notice>
      ) : null}
      {change.done === "uploaded" ? (
        <Notice>
          <Trans>Uploaded. Machines show the logo when they next register.</Trans>
        </Notice>
      ) : null}
      {change.done === "removed" ? (
        <Notice>
          <Trans>Removed. Machines show no logo when they next register.</Trans>
        </Notice>
      ) : null}

      <RemoveLogoDialog
        isOpen={change.confirmingRemoval}
        onOpenChange={change.setConfirmingRemoval}
        onConfirm={change.remove}
      />
    </>
  );
}
