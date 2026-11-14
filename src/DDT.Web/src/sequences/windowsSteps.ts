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

// Version 8. Reads the hardware hash in Windows and hands it to the server, which uploads it to Autopilot. A null group
// tag takes the Autopilot settings' default. waitForImport waits for Autopilot, at most waitMinutes.
export interface RegisterAutopilotStep extends StepBase {
  kind: "registerAutopilot";
  groupTag: string | null;
  waitForImport: boolean;
  waitMinutes: number;
}

// What a BitLocker step does on a machine without a TPM, or in a boot image without manage-bde.
export type BitLockerUnavailable = "Skip" | "Fail";

// Version 8. Starts encrypting the empty Windows partition in Windows PE, between Partition and Apply image.
export interface PreprovisionBitLockerStep extends StepBase {
  kind: "preprovisionBitLocker";
  ifUnavailable: BitLockerUnavailable;
}

// Version 8. Adds the TPM protector and a recovery password in Windows, hands the password to the server, backs it up
// to Active Directory on a joined machine, then turns protection on. waitForEncryption waits, at most waitMinutes.
export interface EnableBitLockerStep extends StepBase {
  kind: "enableBitLocker";
  ifNoTpm: BitLockerUnavailable;
  backupToActiveDirectory: boolean;
  waitForEncryption: boolean;
  waitMinutes: number;
}

export type BitLockerStep = PreprovisionBitLockerStep | EnableBitLockerStep;
