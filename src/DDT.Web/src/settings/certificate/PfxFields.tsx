// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { FileTrigger } from "react-aria-components";

import { Button } from "@/ui/Button";
import { FieldErrorText } from "@/ui/FieldErrorText";
import { TextField } from "@/ui/TextField";

interface PfxFieldsProps {
  fileName: string | null;
  onFile: (file: File | null) => void;
  errors: string[];
  password: string;
  onPasswordChange: (password: string) => void;
}

// A PFX file and its password, the other way to upload a certificate with its key.
export function PfxFields({
  fileName,
  onFile,
  errors,
  password,
  onPasswordChange,
}: PfxFieldsProps) {
  return (
    <>
      <span className="flex flex-wrap items-center gap-3">
        <FileTrigger
          acceptedFileTypes={[".pfx", ".p12"]}
          onSelect={(files) => {
            onFile(files?.[0] ?? null);
          }}
        >
          <Button size="sm">
            <Trans>Choose a PFX file</Trans>
          </Button>
        </FileTrigger>
        <span className="type-small text-ink-2">{fileName ?? <Trans>No file chosen</Trans>}</span>
      </span>
      <FieldErrorText errors={errors} />
      <TextField
        label={<Trans>PFX password</Trans>}
        type="password"
        autoComplete="off"
        value={password}
        onChange={onPasswordChange}
        className="max-w-80"
      />
    </>
  );
}
