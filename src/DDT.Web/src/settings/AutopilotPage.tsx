// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { AdministratorsOnly } from "@/auth/AdministratorsOnly";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";

import { AutopilotSettingsForm } from "./autopilot/AutopilotSettingsForm";

// Machines > Autopilot: the app registration that uploads hardware hashes. Without it, the hashes stay on the server
// for the CSV export. The client secret is never shown, so only administrators see the page.
export function AutopilotPage() {
  return (
    <AdministratorsOnly
      title={<Trans>Autopilot</Trans>}
      refusal={<Trans>Only administrators set up Autopilot uploads.</Trans>}
      className="max-w-[72rem]"
    >
      {() => (
        <Page className="max-w-[72rem]">
          <PageHeader title={<Trans>Autopilot</Trans>} />
          <AutopilotSettingsForm />
        </Page>
      )}
    </AdministratorsOnly>
  );
}
