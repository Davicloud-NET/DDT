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

export interface CreateImageUploadRequest {
  fileName: string;
  length: number;
  // File.lastModified. With the name and length it recognises the same file selected again.
  lastModified: number;
}

export interface ImageUploadSession {
  id: string;
  fileName: string;
  length: number;
  lastModified: number;
  offset: number;
  chunkBytes: number;
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
