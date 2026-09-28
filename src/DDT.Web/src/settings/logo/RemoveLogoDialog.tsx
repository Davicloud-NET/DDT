// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { ConfirmDialog } from "@/ui/ConfirmDialog";

export function RemoveLogoDialog({
  isOpen,
  onOpenChange,
  onConfirm,
}: {
  isOpen: boolean;
  onOpenChange: (isOpen: boolean) => void;
  onConfirm: () => void;
}) {
  return (
    <ConfirmDialog
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      title={<Trans>Remove the logo?</Trans>}
      confirmLabel={<Trans>Remove logo</Trans>}
      onConfirm={onConfirm}
    >
      <p>
        <Trans>
          Machines show no logo on their console from their next registration. Upload it again to
          bring it back.
        </Trans>
      </p>
    </ConfirmDialog>
  );
}
