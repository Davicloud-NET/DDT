// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";

import { percentOf } from "@/lib/format";
import type { UploadOutcome } from "@/uploads/resumableUpload";
import { UploadPanel } from "@/uploads/UploadPanel";

import { imagesQuery, type ImageSummary } from "./images";

const imageKinds = ["Image"] as const;

function describeResult(fileName: string, outcome: UploadOutcome, images: ImageSummary[]): string {
  const count = images.length;
  const added = `${String(count)} ${count === 1 ? "image" : "images"}`;

  switch (outcome) {
    case "added":
      return `Added ${added} from ${fileName}.`;
    case "duplicate":
      return `Every image in ${fileName} is already in the library.`;
    case "unclear":
      return `The library now holds ${added} from ${fileName}.`;
  }
}

// A WIM or ESD file, each of whose x64 images becomes an entry in the library.
export function ImageUpload() {
  const queryClient = useQueryClient();

  return (
    <UploadPanel<ImageSummary[]>
      kind="Image"
      kinds={imageKinds}
      fileLabel="WIM or ESD file"
      accept=".wim,.esd"
      hint="Each x64 Windows image in the file becomes an entry in the library."
      verifyingHint="The server checks the file and reads the images in it. This takes a few minutes for a large file."
      leaveWhileVerifying={(fileName) =>
        `The server goes on checking ${fileName} after you leave and adds its images to the library when it finishes. If it refuses the file, you do not see why.`
      }
      resumeText={(session) =>
        `Select ${session.fileName} again to resume (${String(percentOf(session.offset, session.length))}%).`
      }
      describeResult={describeResult}
      onAdded={() => {
        void queryClient.invalidateQueries({ queryKey: imagesQuery.queryKey });
      }}
    />
  );
}
