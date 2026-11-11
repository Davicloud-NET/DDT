// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useId, useState } from "react";

import { formatBytes } from "@/lib/format";
import { Button } from "@/ui/Button";
import { FieldErrorText } from "@/ui/FieldErrorText";

import { ApplicationUploadPanel } from "../add/ApplicationUploadPanel";
import type { ApplicationFiles } from "../applications";

interface FilesFieldProps {
  files: ApplicationFiles | null;
  // An MSIX package needs its files. A command may run without any.
  isRequired: boolean;
  errors: readonly string[];
  onChange: (files: ApplicationFiles | null) => void;
}

// The application's uploaded files, replaced by uploading others. The old ones go when the application is saved.
export function FilesField({ files, isRequired, errors, onChange }: FilesFieldProps) {
  const labelId = useId();
  const [uploading, setUploading] = useState(false);
  const showsUpload = uploading || (files === null && isRequired);

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      data-field="packageId"
      className="flex flex-col gap-2"
    >
      <span id={labelId} className="type-label text-ink">
        <Trans>Files</Trans>
      </span>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-key bg-well px-3 py-2">
        {files === null ? (
          <span className="flex-1 type-small text-muted">
            <Trans>None</Trans>
          </span>
        ) : (
          <span className="flex min-w-0 flex-1 flex-col">
            <span className="truncate type-data text-ink">{files.name}</span>
            <span className="type-small text-muted">{formatBytes(files.sizeBytes)}</span>
          </span>
        )}
        {uploading ? (
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              setUploading(false);
            }}
          >
            <Trans>Cancel</Trans>
          </Button>
        ) : showsUpload ? null : (
          <Button
            size="sm"
            onPress={() => {
              setUploading(true);
            }}
          >
            {files === null ? <Trans>Upload files</Trans> : <Trans>Replace</Trans>}
          </Button>
        )}
        {files !== null && !isRequired && !uploading ? (
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              onChange(null);
            }}
          >
            <Trans>Remove</Trans>
          </Button>
        ) : null}
      </div>
      {showsUpload ? (
        <ApplicationUploadPanel
          onUploaded={(upload) => {
            onChange(upload.files);
            setUploading(false);
          }}
        />
      ) : null}
      <FieldErrorText errors={errors} />
    </div>
  );
}
