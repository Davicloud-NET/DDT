// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { TextField } from "@/ui/TextField";

export function AuthenticatorCodeField({
  code,
  onChange,
}: {
  code: string;
  onChange: (code: string) => void;
}) {
  return (
    <TextField
      label={<Trans>Code from the authenticator app</Trans>}
      inputMode="numeric"
      autoComplete="one-time-code"
      mono
      value={code}
      onChange={onChange}
      className="max-w-60"
    />
  );
}
