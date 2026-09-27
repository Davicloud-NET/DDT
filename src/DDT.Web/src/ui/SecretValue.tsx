// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useId, useState, type ReactNode } from "react";

import { Button } from "./Button";

// A secret the server shows once, such as a one-time password or an API token: in the mono face on a well, whole and
// selectable, with a key that copies it. Whoever shows it forgets it when its dialog closes.
export function SecretValue({ label, value }: { label: ReactNode; value: string }) {
  const id = useId();
  const [copy, setCopy] = useState<"idle" | "copied" | "failed">("idle");

  async function copyValue() {
    try {
      await navigator.clipboard.writeText(value);
      setCopy("copied");
    } catch {
      // The clipboard is only there on HTTPS and localhost, and a browser may refuse it.
      setCopy("failed");
    }
  }

  return (
    <div className="flex flex-col gap-1.5">
      <span id={id} className="type-label text-ink">
        {label}
      </span>
      <div className="flex items-start gap-2">
        <output
          aria-labelledby={id}
          className="min-w-0 flex-1 rounded-key bg-well px-3 py-2 type-data break-all text-ink select-all"
        >
          {value}
        </output>
        <Button onPress={() => void copyValue()}>
          {copy === "copied" ? <Trans>Copied</Trans> : <Trans>Copy</Trans>}
        </Button>
      </div>
      {copy === "failed" ? (
        <span className="type-small text-fail-text">
          <Trans>The browser did not let DDT copy it. Select it and copy it yourself.</Trans>
        </span>
      ) : null}
    </div>
  );
}
