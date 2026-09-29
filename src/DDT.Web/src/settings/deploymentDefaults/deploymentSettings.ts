// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SettingsForm } from "../useSettingsForm";

// The deployment section. The passwords of the local administrator and the join account are secrets.
export interface DeploymentSettings {
  timeZone: string | null;
  locale: string | null;
  keyboard: string | null;
  consoleLanguage: string | null;
  localAdministrator: { name: string };
  domain: {
    name: string | null;
    organizationalUnit: string | null;
    userName: string | null;
    controller: string | null;
  };
}

export type DeploymentForm = SettingsForm<DeploymentSettings>;
