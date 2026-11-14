// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { StepBase } from "./sequences";

// The steps of the installed Windows and of servicing it in Windows PE, from version 7 on.

// Version 7. Turns on Windows roles and features with DISM, by the names DISM lists, such as NetFx3.
export interface EnableFeaturesStep extends StepBase {
  kind: "enableFeatures";
  features: string[];
}

// Version 7. Installs updates from Microsoft Update until none is left, restarting as they ask. Never a feature upgrade.
export interface WindowsUpdateStep extends StepBase {
  kind: "windowsUpdate";
  drivers: boolean;
  optionalUpdates: boolean;
  microsoftProducts: boolean;
  excludedKbs: string[];
  restartLimit: number;
}

// Version 8. Adds Windows packages to the applied Windows with DISM in Windows PE: language packs, then features, then
// updates. matchingUpdates adds every update marked for the image's build. cleanup removes what the updates replaced.
export interface ServicePackagesStep extends StepBase {
  kind: "servicePackages";
  packages: string[];
  matchingUpdates: boolean;
  cleanup: boolean;
}
