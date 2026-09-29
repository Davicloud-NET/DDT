// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { ReauthDialog } from "@/settings/parts/ReauthDialog";

interface ReauthForAccountsProps {
  isOpen: boolean;
  onAccepted: () => void;
  onCancel: () => void;
  confirmLabel?: ReactNode;
}

// The server only accepts a change to an account after the person making it enters their password again.
export function ReauthForAccounts({
  isOpen,
  onAccepted,
  onCancel,
  confirmLabel,
}: ReauthForAccountsProps) {
  return (
    <ReauthDialog
      isOpen={isOpen}
      onAccepted={onAccepted}
      onCancel={onCancel}
      {...(confirmLabel === undefined ? {} : { confirmLabel })}
      reason={
        <Trans>
          An account reaches machines with whatever it may do in the domain, so changing one needs
          your password again.
        </Trans>
      }
    />
  );
}
