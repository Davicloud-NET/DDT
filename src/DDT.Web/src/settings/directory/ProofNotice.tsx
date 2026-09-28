// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";

// Asks an administrator who signs in through the directory to test their own sign-in first. The save decides whether
// they still can.
export function ProofNotice({ stale }: { stale: boolean }) {
  return (
    <Notice tone="attention">
      {stale ? (
        <Trans>
          Your test of these values is more than 5 minutes old. Test your sign-in again before you
          save.
        </Trans>
      ) : (
        <Trans>
          You sign in through the directory, and these changes decide whether you still can. Test
          your own sign-in with them above before you save; the save is accepted within 5 minutes of
          a test that kept you an administrator.
        </Trans>
      )}
    </Notice>
  );
}
