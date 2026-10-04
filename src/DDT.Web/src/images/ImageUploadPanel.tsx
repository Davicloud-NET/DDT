// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";

import { UploadPanel } from "@/uploads/UploadPanel";

import { imagesQuery, type ImageSummary } from "./images";
import { describeResult } from "./imageView";

const imageKinds = ["Image"] as const;

// Takes WIM, ESD, ISO and disk image files. The images in the server's answer are added to the list.
export function ImageUploadPanel() {
  const queryClient = useQueryClient();

  return (
    <UploadPanel<ImageSummary[]>
      kind="Image"
      kinds={imageKinds}
      what={<Trans>a WIM, ESD, ISO or disk image file</Trans>}
      accept={[".wim", ".esd", ".iso", ".img", ".raw", ".gz", ".xz", ".zst", ".qcow2"]}
      hint={
        <Trans>
          Each x64 Windows image in a WIM or ESD file becomes an entry, and a Windows ISO is read
          for the install.wim or install.esd in it. A disk image, such as a Linux cloud image,
          becomes one: raw, compressed with gzip, zstd or xz, or qcow2. Convert VHDX, VMDK or VDI to
          raw with qemu-img first.
        </Trans>
      }
      verifyingHint={
        <Trans>
          The server checks the file, reads the images in it and compresses a disk image. This takes
          a few minutes for a large file.
        </Trans>
      }
      leaveWhileVerifying={(file) =>
        t`The server goes on checking ${file} after you leave and adds its images to the library when it finishes. If it refuses the file, you do not see why.`
      }
      describeResult={describeResult}
      onAdded={(added) => {
        queryClient.setQueryData(imagesQuery.queryKey, (existing) =>
          existing === undefined
            ? existing
            : [...existing.filter((image) => !added.some((a) => a.id === image.id)), ...added],
        );
      }}
    />
  );
}
