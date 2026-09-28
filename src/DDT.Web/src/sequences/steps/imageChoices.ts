// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { isDeployable, type ImageSummary } from "@/images/images";

import type { Choice } from "../fields/fieldBase";

export function windowsImageChoice(image: ImageSummary): Choice {
  const architecture = image.architecture ?? t`unknown architecture`;
  const facts = [image.language, architecture].filter((fact) => fact !== null).join(", ");

  return {
    id: image.id,
    label: image.name,
    description: isDeployable(image) ? facts : t`${facts}, cannot be installed`,
    isDisabled: !isDeployable(image),
  };
}

export function rawImageChoice(image: ImageSummary): Choice {
  const architecture = image.architecture ?? t`another processor`;
  const signature =
    image.bootCapability === "SecureBootOk"
      ? t`Signed for Secure Boot`
      : image.bootCapability === "NotSigned"
        ? t`Not signed for Secure Boot`
        : t`Not known whether it is signed for Secure Boot`;

  return {
    id: image.id,
    label: image.name,
    description: isDeployable(image) ? signature : t`For ${architecture} only, cannot be written`,
    isDisabled: !isDeployable(image),
  };
}

// What an image that may not start with Secure Boot on means for the machines, or null for one that does.
export function secureBootWarning(image: ImageSummary): string | null {
  switch (image.bootCapability) {
    case "NotSigned":
      return t`This image will not start with Secure Boot on. Turn Secure Boot off in the machine's firmware setup, or enroll your own key.`;
    case "Unknown":
    case null:
      return t`This image may not start with Secure Boot on, as DDT could not tell whether it is signed for it. Turn Secure Boot off in the machine's firmware setup, or enroll your own key.`;
    case "SecureBootOk":
      return null;
  }
}
