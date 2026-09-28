// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { AdministratorsOnly } from "@/auth/AdministratorsOnly";

import { ServerSettings } from "./server/ServerSettings";

// Administration > Server: what configuration decides, how every settings section stands, and tabs for the
// certificate, the proxies, the agent and the log levels. The server refuses everyone but administrators.
export function ServerPage() {
  return (
    <AdministratorsOnly
      title={<Trans>Server</Trans>}
      refusal={<Trans>Only administrators see and change the server's settings.</Trans>}
    >
      {() => <ServerSettings />}
    </AdministratorsOnly>
  );
}
