// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { ReauthDialog } from "../parts/ReauthDialog";
import type { GuardedAction } from "../useGuardedAction";

// The password again, which Generate and Upload both need.
export function CertificateProof({
  action,
  confirmLabel,
}: {
  action: GuardedAction;
  confirmLabel: ReactNode;
}) {
  return (
    <ReauthDialog
      isOpen={action.needsReauth}
      onAccepted={action.retryAfterReauth}
      onCancel={action.cancelReauth}
      confirmLabel={confirmLabel}
      reason={
        <Trans>
          The server certificate decides what browsers and machines trust, so replacing it needs
          your password again.
        </Trans>
      }
    />
  );
}
