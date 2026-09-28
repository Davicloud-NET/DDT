// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { formattingLocale } from "@/i18n/i18n";

import { clockNote, type MachineLogEntry } from "./log";
import { levelLabel } from "./logLabels";

// A row shows one line of a message; this shows all of it with every time the server knows.
export function LineDetail({
  line,
  step,
  onClose,
}: {
  line: MachineLogEntry;
  step: string | null;
  onClose: () => void;
}) {
  const { t: translate } = useLingui();
  const locale = formattingLocale();
  const note = clockNote(line);
  const at = new Date(line.timestampUtc).toLocaleString(locale);
  const received = new Date(line.receivedUtc).toLocaleString(locale);
  const level = levelLabel(line.level);

  return (
    <section
      aria-label={translate`Log line`}
      className="flex flex-col gap-2 rounded-key bg-well p-3 shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
    >
      <div className="flex items-start gap-3">
        <p className="flex-1 type-small text-ink-2">
          {step === null ? (
            <Trans>
              {level} at {at}. Received {received}.
            </Trans>
          ) : (
            <Trans>
              {level} at {at}, during {step}. Received {received}.
            </Trans>
          )}
          {note !== null ? ` ${note}` : null}
        </p>
        <AriaButton
          aria-label={translate`Close the line`}
          onPress={onClose}
          className="flex size-7 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
        >
          <IconX size={16} stroke={2} />
        </AriaButton>
      </div>
      <pre className="max-h-64 overflow-auto type-data whitespace-pre-wrap text-ink">
        {line.message}
      </pre>
    </section>
  );
}
