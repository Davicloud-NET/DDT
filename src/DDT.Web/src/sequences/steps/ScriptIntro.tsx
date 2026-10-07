// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { RunScriptStep } from "../sequences";

// Who a script runs as, and that everyone can read it.
export function ScriptIntro({ step }: { step: RunScriptStep }) {
  return (
    <p className="text-ink-2 sm:col-span-2">
      {(step.runAs ?? null) !== null ? (
        <Trans>
          Runs as the account chosen below, which DDT signs in with; the script never sees its
          password. Everyone who can sign in to DDT can read the script, so it must hold no
          passwords.
        </Trans>
      ) : step.phase === "LinuxBoot" ? (
        <Trans>
          Runs as root in the Linux boot environment, before the disk image starts. Everyone who can
          sign in to DDT can read the script, so it must hold no passwords.
        </Trans>
      ) : (
        <Trans>
          Runs as SYSTEM on the machine. Everyone who can sign in to DDT can read the script, so it
          must hold no passwords.
        </Trans>
      )}
    </p>
  );
}
