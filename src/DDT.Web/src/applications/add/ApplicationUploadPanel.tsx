// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { UploadPanel } from "@/uploads/UploadPanel";

import type { ApplicationUpload } from "../applications";

function describeResult(fileName: string): string {
  return t`Uploaded ${fileName}.`;
}

// Takes any file: a single installer, which the server zips, or a zip of a folder.
export function ApplicationUploadPanel({
  onUploaded,
}: {
  onUploaded: (upload: ApplicationUpload) => void;
}) {
  return (
    <UploadPanel<ApplicationUpload>
      kind="Application"
      kinds={["Application"]}
      what={<Trans>an installer or a zip file</Trans>}
      accept={[]}
      hint={null}
      describeResult={describeResult}
      onAdded={onUploaded}
    />
  );
}
