// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

export function BootFileHint({ http, port }: { http: boolean; port: number | null }) {
  const portText = port === null ? "" : String(port);

  if (!http) {
    return (
      <Trans>
        A path in the boot directory. Machines that no longer trust the Microsoft 2011 CA need
        x64/bootmgfw_ex.efi.
      </Trans>
    );
  }

  return port === null ? (
    <Trans>
      An http URL on DDT's boot port, below /boot/. Machines that no longer trust the Microsoft 2011
      CA need bootmgfw_ex.efi.
    </Trans>
  ) : (
    <Trans>
      An http URL on port {portText}, below /boot/. Machines that no longer trust the Microsoft 2011
      CA need bootmgfw_ex.efi.
    </Trans>
  );
}
