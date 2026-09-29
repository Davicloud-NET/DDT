// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";
import { FileTrigger } from "react-aria-components";

import { Button } from "@/ui/Button";
import { TextField } from "@/ui/TextField";

// A PEM block to paste, or to load from a file.
export function PemField({
  label,
  chooseLabel,
  value,
  onChange,
  errors,
}: {
  label: ReactNode;
  chooseLabel: string;
  value: string;
  onChange: (value: string) => void;
  errors: string[];
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <TextField
        label={label}
        multiline
        rows={5}
        mono
        spellCheck="false"
        autoComplete="off"
        placeholder="-----BEGIN ..."
        value={value}
        onChange={onChange}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
      />
      <span>
        <FileTrigger
          acceptedFileTypes={[".pem", ".crt", ".cer", ".key"]}
          onSelect={(files) => {
            const file = files?.[0];

            if (file !== undefined) {
              void file.text().then(onChange);
            }
          }}
        >
          <Button size="sm" variant="quiet" aria-label={chooseLabel}>
            <Trans>Load a file</Trans>
          </Button>
        </FileTrigger>
      </span>
    </div>
  );
}
