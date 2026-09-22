// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet } from "@/lib/api";

export type ImageKind = "Wim";

export interface ImageSummary {
  id: string;
  name: string;
  kind: ImageKind;
  sha256: string;
  sizeBytes: number;
  wimIndex: number;
  edition: string | null;
  // "x86", "x64", "arm64", or null when the WIM does not say.
  architecture: string | null;
  version: string | null;
  language: string | null;
  installedBytes: number;
  originalFileName: string | null;
  uploadedUtc: string;
  uploadedBy: string | null;
}

// What a completed upload becomes: images from a WIM, or a package from a zip of drivers or of files.
export type UploadKind = "Image" | "Drivers" | "Files";

export interface CreateImageUploadRequest {
  fileName: string;
  length: number;
  // File.lastModified. With the name, length and kind it recognises the same file selected again.
  lastModified: number;
  // Image when left out.
  kind?: UploadKind;
}

export interface ImageUploadSession {
  id: string;
  fileName: string;
  length: number;
  lastModified: number;
  offset: number;
  chunkBytes: number;
  // The server always sends it; a session without one is an image's.
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

// The agent runs in x64 Windows PE and applies the image's own boot files, so only x64 images deploy.
export function isDeployable(image: ImageSummary): boolean {
  return image.architecture === "x64";
}

export function deleteImage(id: string): Promise<void> {
  return apiDelete(`/api/images/${id}`);
}

export function discardUpload(id: string): Promise<void> {
  return apiDelete(`/api/images/uploads/${id}`);
}
