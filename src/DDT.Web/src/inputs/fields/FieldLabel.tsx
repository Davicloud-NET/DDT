// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { AskedInput } from "@/inputs/inputs";

// The input's label, marked where an answer is required.
export function FieldLabel({ input }: { input: AskedInput }) {
  const label = input.label;

  return input.required ? (
    <Trans>
      {label} <span className="font-normal text-muted">(required)</span>
    </Trans>
  ) : (
    label
  );
}
