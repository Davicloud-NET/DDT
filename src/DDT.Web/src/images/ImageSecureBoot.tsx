// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { StateTag, type StateTone } from "@/ui/StateTag";

import { bootCapabilityLabel, type ImageSummary } from "./images";

const bootTone: Record<NonNullable<ImageSummary["bootCapability"]>, StateTone> = {
  SecureBootOk: "ok",
  NotSigned: "attention",
  Unknown: "idle",
};

// The Secure Boot column: a raw disk image's capability, or the boot files Microsoft signs for a Windows image.
export function ImageSecureBoot({ image }: { image: ImageSummary }) {
  return image.kind === "RawDisk" && image.bootCapability !== null ? (
    <StateTag tone={bootTone[image.bootCapability]}>{bootCapabilityLabel(image)}</StateTag>
  ) : (
    <span className="type-small text-muted">
      <Trans>Microsoft's boot files</Trans>
    </span>
  );
}
