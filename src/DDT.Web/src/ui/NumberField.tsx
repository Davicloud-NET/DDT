// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconMinus, IconPlus } from "@tabler/icons-react";
import type { ReactNode } from "react";
import {
  Button as AriaButton,
  FieldError,
  Group,
  Input,
  Label,
  NumberField as AriaNumberField,
  Text,
  type NumberFieldProps as AriaNumberFieldProps,
} from "react-aria-components";

import { cx } from "./cx";
import { fieldClass } from "./TextField";

// A whole number with a unit, for sizes, timeouts and exit codes. Its stepper buttons step it, and a typed value is
// checked when the field loses focus.
export function NumberField({
  label,
  hint,
  errorMessage,
  className,
  ...props
}: Omit<AriaNumberFieldProps, "className"> & {
  label: ReactNode;
  hint?: ReactNode;
  errorMessage?: ReactNode;
  className?: string;
}) {
  const { t } = useLingui();
  // The steppers sit inside the field's frame, so a press darkens them without sinking them.
  const stepper =
    "flex w-8 cursor-pointer items-center justify-center text-muted motion-colors outline-none hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed pressed:duration-(--duration-press) disabled:opacity-40 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus";

  return (
    <AriaNumberField {...props} className={cx("flex flex-col gap-1.5", className)}>
      <Label className="type-label text-ink">{label}</Label>
      <Group
        className={cx(
          fieldClass,
          "flex h-9.5 overflow-hidden px-0 focus-within:shadow-[inset_0_0_0_2px_var(--color-focus)] focus-within:duration-0",
        )}
      >
        <Input className="min-w-0 flex-1 bg-transparent px-2.5 type-data outline-none" />
        <AriaButton
          slot="decrement"
          aria-label={t`Less`}
          className={cx(stepper, "border-l border-line-soft")}
        >
          <IconMinus size={14} stroke={2} />
        </AriaButton>
        <AriaButton
          slot="increment"
          aria-label={t`More`}
          className={cx(stepper, "border-l border-line-soft")}
        >
          <IconPlus size={14} stroke={2} />
        </AriaButton>
      </Group>
      {hint ? (
        <Text slot="description" className="type-small text-muted">
          {hint}
        </Text>
      ) : null}
      <FieldError className="type-small text-fail-text">{errorMessage}</FieldError>
    </AriaNumberField>
  );
}
