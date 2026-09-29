// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import {
  FieldError,
  Label,
  RadioButton,
  RadioField,
  RadioGroup,
  Text,
} from "react-aria-components";

import { cx } from "@/ui/cx";

interface KeyChoiceProps {
  label: ReactNode;
  choices: { value: string; label: string }[];
  value: string;
  onChange: (value: string) => void;
  isRequired: boolean;
  hint: string | null;
  error: string | null;
}

// A choice among a few answers as a row of buttons. The chosen one is raised out of its well, like the filters.
export function KeyChoice({
  label,
  choices,
  value,
  onChange,
  isRequired,
  hint,
  error,
}: KeyChoiceProps) {
  return (
    <RadioGroup
      value={value === "" ? null : value}
      onChange={onChange}
      isRequired={isRequired}
      isInvalid={error !== null}
      orientation="horizontal"
      className="flex flex-col gap-1.5"
    >
      <Label className="type-label text-ink">{label}</Label>
      <div className="flex w-fit max-w-full flex-wrap gap-0.5 rounded-panel bg-well p-0.75 shadow-[inset_0_0_0_1px_var(--color-line-soft)]">
        {choices.map((choice) => (
          <RadioField key={choice.value} value={choice.value} className="flex">
            <RadioButton
              className={cx(
                "flex h-8 cursor-pointer items-center rounded-key px-2.75 type-label font-semibold text-ink-2 motion-colors outline-none",
                "hover:text-ink selected:bg-raised selected:text-ink selected:shadow-[0_0_0_1px_var(--color-line)]",
                "focus-visible:outline-2 focus-visible:outline-focus",
              )}
            >
              {choice.label}
            </RadioButton>
          </RadioField>
        ))}
      </div>
      {hint !== null ? (
        <Text slot="description" className="type-small text-muted">
          {hint}
        </Text>
      ) : null}
      <FieldError className="type-small text-fail-text">{error}</FieldError>
    </RadioGroup>
  );
}
