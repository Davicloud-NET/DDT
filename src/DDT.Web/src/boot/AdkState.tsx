// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation } from "@tanstack/react-query";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { Button } from "@/ui/Button";
import { ConfirmDialog } from "@/ui/ConfirmDialog";
import { Notice } from "@/ui/Notice";

import { installAdk, type BootImageAdk } from "./bootImage";

// The Windows ADK on the server, which a build needs. Without one, an administrator installs it from here.
export function AdkState({ adk, isBusy }: { adk: BootImageAdk; isBusy: boolean }) {
  const administrator = useIsAdministrator();
  const [confirming, setConfirming] = useState(false);
  const install = useMutation({ mutationFn: installAdk });
  const version = adk.version ?? "?";

  if (adk.installed && adk.supported) {
    return (
      <p className="type-small text-muted">
        <Trans>Windows ADK with the Windows PE add-on {version}</Trans>
      </p>
    );
  }

  if (adk.installed) {
    return (
      <Notice tone="attention">
        <Trans>
          The Windows PE add-on {version} is too old. Uninstall it and the Windows ADK, then install
          them from here.
        </Trans>
      </Notice>
    );
  }

  return (
    <>
      <Notice
        tone="attention"
        actions={
          administrator ? (
            <Button
              size="sm"
              isDisabled={isBusy || install.isPending}
              onPress={() => {
                install.reset();
                setConfirming(true);
              }}
            >
              <Trans>Install the Windows ADK</Trans>
            </Button>
          ) : null
        }
      >
        <Trans>This server has no Windows ADK with the Windows PE add-on.</Trans>
      </Notice>
      {install.isError ? <Notice tone="fail">{install.error.message}</Notice> : null}

      <ConfirmDialog
        isOpen={confirming}
        onOpenChange={setConfirming}
        title={<Trans>Install the Windows ADK?</Trans>}
        confirmLabel={<Trans>Install</Trans>}
        onConfirm={() => {
          setConfirming(false);
          install.mutate();
        }}
      >
        <p>
          <Trans>
            The server downloads the Windows ADK and its Windows PE add-on from Microsoft, about 4
            GB, which takes a few minutes.
          </Trans>
        </p>
        <p>
          <Trans>Installing accepts Microsoft's licence terms for both.</Trans>
        </p>
      </ConfirmDialog>
    </>
  );
}
