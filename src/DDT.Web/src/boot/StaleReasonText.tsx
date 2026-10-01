// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { BootImageView, StaleReason } from "./bootImage";

// One reason the served build no longer fits the server, with what the build recorded.
export function StaleReasonText({ reason, view }: { reason: StaleReason; view: BootImageView }) {
  const address = view.build?.serverUrl ?? "?";
  const builtWith = view.build?.adkVersion ?? "?";
  const installed = view.builder.adk?.version ?? "?";

  switch (reason) {
    case "drivers":
      return view.build === null ? (
        <Trans>
          Drivers are flagged for Windows PE, but no boot image built with this version of the build
          script is in the boot directory.
        </Trans>
      ) : (
        <Trans>The drivers flagged for Windows PE have changed since it was built.</Trans>
      );
    case "serverAddress":
      return (
        <Trans>
          It names the server {address}, a name or port the server no longer has. Machines that
          netboot it cannot reach the server.
        </Trans>
      );
    case "root":
      return (
        <Trans>
          It trusts another root certificate than this server's. Machines that netboot it cannot
          reach the server.
        </Trans>
      );
    case "adk":
      return (
        <Trans>
          It came from the Windows ADK {builtWith}, and the server now has {installed}.
        </Trans>
      );
  }
}
