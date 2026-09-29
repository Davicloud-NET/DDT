// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { formatBytes } from "@/lib/format";
import { Facts } from "@/ui/Facts";
import { Notice } from "@/ui/Notice";

import {
  bootCapabilityLabel,
  isDeployable,
  kindLabel,
  secureBootWarning,
  type ImageSummary,
} from "./images";
import { imageSource, installedLine } from "./imageView";

// An image's details drawer.
export function ImageFacts({ image }: { image: ImageSummary }) {
  const warning = secureBootWarning(image);
  const unknown = t`Not stated`;

  return (
    <>
      <Facts
        items={[
          { label: <Trans>Kind</Trans>, value: kindLabel(image.kind) },
          ...(image.edition === null
            ? []
            : [{ label: <Trans>Edition</Trans>, value: image.edition }]),
          { label: <Trans>Architecture</Trans>, value: image.architecture ?? unknown },
          { label: <Trans>Version</Trans>, value: image.version ?? unknown },
          { label: <Trans>Language</Trans>, value: image.language ?? unknown },
          { label: <Trans>File</Trans>, value: imageSource(image) },
          { label: <Trans>Size</Trans>, value: formatBytes(image.sizeBytes) },
          { label: <Trans>Installed</Trans>, value: installedLine(image) },
          { label: <Trans>SHA-256</Trans>, value: image.sha256, mono: true },
          ...(image.sourceSha256 === null
            ? []
            : [{ label: <Trans>Disk SHA-256</Trans>, value: image.sourceSha256, mono: true }]),
          ...(image.kind === "RawDisk"
            ? [{ label: <Trans>Secure Boot</Trans>, value: bootCapabilityLabel(image) ?? unknown }]
            : []),
        ]}
      />
      {image.kind === "RawDisk" && image.bootDetail !== null ? (
        <p className="type-small text-ink-2">{image.bootDetail}</p>
      ) : null}
      {warning !== null ? <Notice tone="attention">{warning}</Notice> : null}
      {!isDeployable(image) ? (
        <Notice tone="fail">
          <Trans>Not deployable: only x64 images can be installed.</Trans>
        </Notice>
      ) : null}
    </>
  );
}
