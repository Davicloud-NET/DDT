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
        <Trans>Everyone who can sign in can read it, so no passwords.</Trans>
      ) : step.phase === "LinuxBoot" || step.phase === "Linux" ? (
        <Trans>Runs as root. Everyone who can sign in can read it, so no passwords.</Trans>
      ) : (
        <Trans>Runs as SYSTEM. Everyone who can sign in can read it, so no passwords.</Trans>
      )}
    </p>
  );
}
