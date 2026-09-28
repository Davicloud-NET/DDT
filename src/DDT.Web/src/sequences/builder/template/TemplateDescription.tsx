// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import type { ReactNode } from "react";
import { Text } from "react-aria-components";

import { parseTemplate, renderTemplate } from "../../flow/templates";

// What a template field says under it: a name nothing defines before the server finds it, and the template filled in
// for a sample machine.
export function TemplateDescription({
  value,
  hint,
  howTo,
  locked,
  known,
  sample,
  warnings,
}: {
  value: string;
  hint: ReactNode;
  howTo: boolean;
  locked: boolean;
  known: (name: string) => boolean;
  sample: (name: string) => string | null;
  warnings: readonly string[];
}) {
  const { t } = useLingui();
  const parsed = value.includes("{{") ? parseTemplate(value, known) : null;
  const rendered = parsed === null ? null : renderTemplate(value, sample);
  const braces = "{{";
  const example = "{{ComputerName}}";

  return (
    <Text slot="description" className="flex flex-col gap-0.5 type-small text-muted">
      {hint ? <span>{hint}</span> : null}
      {locked || !howTo ? null : (
        <span>{t`Type ${braces} to use a value, such as ${example}.`}</span>
      )}
      {parsed?.problems.map((problem) => (
        <span key={problem.message} className="text-attention-text">
          {problem.message}
        </span>
      ))}
      {rendered !== null && parsed?.problems.length === 0 ? (
        rendered.error === null ? (
          <span>
            {t`On a sample machine:`}{" "}
            <span className="type-data text-ink-2">{rendered.output}</span>
          </span>
        ) : (
          <span className="text-attention-text">{rendered.error.message}</span>
        )
      ) : null}
      {warnings.map((warning) => (
        <span key={warning} className="text-attention-text">
          {warning}
        </span>
      ))}
    </Text>
  );
}
