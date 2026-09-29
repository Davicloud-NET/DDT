// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { resumableUpload, type UploadOptions, type UploadOutcome } from "@/uploads/resumableUpload";

import type { ImageSummary } from "./images";

export { backoff } from "@/lib/backoff";
export {
  MAX_FAILURES,
  type UploadOptions,
  type UploadOutcome,
  type UploadProgress,
} from "@/uploads/resumableUpload";

export interface UploadResult {
  outcome: UploadOutcome;
  images: ImageSummary[];
}

// A WIM becomes one library entry per image in it.
export async function uploadImage(file: File, options: UploadOptions): Promise<UploadResult> {
  const { outcome, library } = await resumableUpload<ImageSummary[]>(file, "Image", options);

  return { outcome, images: library };
}
