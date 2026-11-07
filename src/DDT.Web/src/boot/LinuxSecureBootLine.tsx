// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { describeUefiCas, uefiCaCount } from "@/machines/secureBoot";

// The shim is signed under one or both of Microsoft's CAs, and a firmware that trusts either starts it.
export function LinuxSecureBootLine({ signedUnder }: { signedUnder: string | null }) {
  const cas = describeUefiCas(signedUnder);

  switch (uefiCaCount(signedUnder)) {
    case 0:
      return null;
    case 1:
      return (
        <p className="max-w-[80ch] text-ink-2">
          <Trans>
            With Secure Boot on, a machine starts it when its firmware trusts {cas}. A machine whose
            firmware does not is not started in it.
          </Trans>
        </p>
      );
    default:
      return (
        <p className="max-w-[80ch] text-ink-2">
          <Trans>
            With Secure Boot on, a machine starts it when its firmware trusts one of {cas}. A
            machine whose firmware trusts neither is not started in it.
          </Trans>
        </p>
      );
  }
}
