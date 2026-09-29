// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiDelete, apiGet } from "@/lib/api";
import { removeByIds, upsertById } from "@/lib/listCache";

// A Windows image from a WIM, or a whole disk such as a Linux cloud image.
export type ImageKind = "Wim" | "RawDisk";

// Whether a raw disk image starts on a stock PC with Secure Boot on.
export type ImageBootCapability = "SecureBootOk" | "NotSigned" | "Unknown";

// For a raw disk image, sizeBytes is the stored, compressed file, installedBytes the disk inside it, and wimIndex 0.
export interface ImageSummary {
  id: string;
  name: string;
  kind: ImageKind;
  sha256: string;
  sizeBytes: number;
  wimIndex: number;
  edition: string | null;
  // "x86", "x64", "arm64", or null when the image does not say.
  architecture: string | null;
  version: string | null;
  language: string | null;
  installedBytes: number;
  originalFileName: string | null;
  uploadedUtc: string;
  uploadedBy: string | null;
  // Raw disk images only: the capability, the sentence that explains it, and the uncompressed disk's SHA-256.
  bootCapability: ImageBootCapability | null;
  bootDetail: string | null;
  sourceSha256: string | null;
}

// What a completed upload becomes: images from a WIM, or a package from a zip of drivers or of files.
export type UploadKind = "Image" | "Drivers" | "Files";

export interface CreateImageUploadRequest {
  fileName: string;
  length: number;
  // File.lastModified. With the name, length and kind, it lets the server recognise the same file chosen again.
  lastModified: number;
  // Defaults to Image.
  kind?: UploadKind;
}

export interface ImageUploadSession {
  id: string;
  fileName: string;
  length: number;
  lastModified: number;
  offset: number;
  chunkBytes: number;
  // The server always sends it. A session without it is an image upload.
  kind?: UploadKind;
}

export const imagesQuery = queryOptions({
  queryKey: ["images"],
  queryFn: () => apiGet<ImageSummary[]>("/api/images"),
});

// Uploads that were started and not finished. Only administrators may list them.
export const uploadsQuery = queryOptions({
  queryKey: ["image-uploads"],
  queryFn: () => apiGet<ImageUploadSession[]>("/api/images/uploads"),
});

// The agent runs in x64 WinPE and applies the Windows image's own boot files, so only x64 Windows images deploy. A raw
// disk image deploys unless its boot file is for another processor. One with an unreadable boot file may still start.
export function isDeployable(image: ImageSummary): boolean {
  return image.architecture === "x64" || (image.kind === "RawDisk" && image.architecture === null);
}

export function kindLabel(kind: ImageKind): string {
  return kind === "RawDisk" ? t`Raw disk image` : t`Windows image`;
}

// The Secure Boot column's text for a raw disk image. Null for a Windows image, since Microsoft signs its boot files.
export function bootCapabilityLabel(image: ImageSummary): string | null {
  switch (image.kind === "RawDisk" ? image.bootCapability : null) {
    case "SecureBootOk":
      return t`Signed`;
    case "NotSigned":
      return t`Not signed`;
    case "Unknown":
      return t`Unknown`;
    case null:
      return null;
  }
}

// The warning for a raw disk image that won't, or may not, start with Secure Boot on. Null for every other image.
export function secureBootWarning(image: ImageSummary): string | null {
  if (image.kind !== "RawDisk" || image.bootCapability === "SecureBootOk") {
    return null;
  }

  return image.bootCapability === "NotSigned"
    ? t`This image will not start with Secure Boot on. Turn Secure Boot off in the machine's firmware setup, or enroll your own key.`
    : t`This image may not start with Secure Boot on, as DDT could not tell whether it is signed for it. Turn Secure Boot off in the machine's firmware setup, or enroll your own key.`;
}

export interface ImagesRemoved {
  imageIds: string[];
}

function byName(a: ImageSummary, b: ImageSummary): number {
  return a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
}

export function upsertImage(queryClient: QueryClient, image: ImageSummary): void {
  queryClient.setQueryData(imagesQuery.queryKey, (list) => upsertById(list, image, byName));
}

export function removeImages(queryClient: QueryClient, imageIds: readonly string[]): void {
  queryClient.setQueryData(imagesQuery.queryKey, (list) => removeByIds(list, imageIds));
}

export function deleteImage(id: string): Promise<void> {
  return apiDelete(`/api/images/${id}`);
}

export function discardUpload(id: string): Promise<void> {
  return apiDelete(`/api/images/uploads/${id}`);
}
