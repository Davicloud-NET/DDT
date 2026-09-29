// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";

import { formatBytes } from "@/lib/format";
import type { UploadOutcome } from "@/uploads/resumableUpload";

import { bootCapabilityLabel, kindLabel, type ImageSummary } from "./images";

export function describeResult(
  file: string,
  outcome: UploadOutcome,
  images: ImageSummary[],
): string {
  const count = images.length;

  switch (outcome) {
    case "added":
      return plural(count, {
        one: `Added # image from ${file}.`,
        other: `Added # images from ${file}.`,
      });
    case "duplicate":
      return t`Every image in ${file} is already in the library.`;
    case "unclear":
      return plural(count, {
        one: `The library now holds # image from ${file}.`,
        other: `The library now holds # images from ${file}.`,
      });
  }
}

// A raw disk image is the whole file; a WIM holds several images by index.
export function imageSource(image: ImageSummary): string {
  const file = image.originalFileName ?? t`Unknown file`;
  const index = image.wimIndex;

  return image.kind === "RawDisk" ? file : t`${file}, index ${index}`;
}

export function kindLine(image: ImageSummary): string {
  return [kindLabel(image.kind), image.architecture, image.version, image.language]
    .filter((part): part is string => part !== null && part !== "")
    .join(", ");
}

export function installedLine(image: ImageSummary): string {
  const installed = formatBytes(image.installedBytes);

  return image.kind === "RawDisk" ? t`${installed} disk` : t`${installed} installed`;
}

export function deleteConsequence(image: ImageSummary): string {
  const name = image.name;
  const size = formatBytes(image.sizeBytes);

  return image.kind === "RawDisk"
    ? t`${name} (${size}) is removed from the library and can no longer be used by a task sequence. Its compressed disk is deleted from the server.`
    : t`${name} (${size}) is removed from the library and can no longer be used by a task sequence. The WIM file is deleted from the server once no other image in the library comes from it.`;
}

export function matchesImage(image: ImageSummary, needle: string): boolean {
  return [
    image.name,
    kindLabel(image.kind),
    bootCapabilityLabel(image),
    image.edition,
    image.architecture,
    image.version,
    image.language,
    image.originalFileName,
    image.uploadedBy,
    image.sha256,
  ].some((value) => value?.toLowerCase().includes(needle) === true);
}
