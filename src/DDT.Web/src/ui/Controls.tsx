// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconMinus, IconPlus, IconSearch } from "@tabler/icons-react";
import type { ReactNode } from "react";
import {
  Button as AriaButton,
  FieldError,
  Group,
  Input,
  Label,
  NumberField as AriaNumberField,
  ProgressBar as AriaProgressBar,
  SearchField as AriaSearchField,
  Text,
  ToggleButton,
  ToggleButtonGroup,
  type Key,
  type NumberFieldProps as AriaNumberFieldProps,
  type SearchFieldProps as AriaSearchFieldProps,
} from "react-aria-components";

import { cx } from "./cx";
import { fieldClass } from "./TextField";

// A selector for filtering a list by state: a well holding one key per state, the chosen key raised out of it.
// Each key carries its count; a count that needs someone (waiting, failed) is printed on a signal-coloured tag. The
// keys differ in width, so the raised one does not slide to another: one sinks and the other rises, in colour.
export interface FilterOption {
  id: string;
  label: ReactNode;
  count?: number;
  tone?: "attention" | "fail";
}

export function FilterSelector({
  label,
  options,
  selected,
  onChange,
}: {
  label: string;
  options: FilterOption[];
  selected: string;
  onChange: (id: string) => void;
}) {
  return (
    <ToggleButtonGroup
      aria-label={label}
      selectionMode="single"
      disallowEmptySelection
      selectedKeys={[selected]}
      onSelectionChange={(keys: Set<Key>) => {
        const [key] = keys;

        if (key !== undefined) {
          onChange(String(key));
        }
      }}
      className={wellClass}
    >
      {options.map((option) => (
        <FilterKey key={option.id} option={option} />
      ))}
    </ToggleButtonGroup>
  );
}

// The same well for filters that combine, such as the levels of a log: every key toggles on its own, and at
// least one stays on.
export function FilterChips({
  label,
  options,
  selected,
  onChange,
}: {
  label: string;
  options: FilterOption[];
  selected: ReadonlySet<string>;
  onChange: (selected: Set<string>) => void;
}) {
  return (
    <ToggleButtonGroup
      aria-label={label}
      selectionMode="multiple"
      disallowEmptySelection
      selectedKeys={selected}
      onSelectionChange={(keys: Set<Key>) => {
        onChange(new Set([...keys].map(String)));
      }}
      className={wellClass}
    >
      {options.map((option) => (
        <FilterKey key={option.id} option={option} />
      ))}
    </ToggleButtonGroup>
  );
}

const wellClass =
  "flex flex-wrap gap-0.5 rounded-panel bg-well p-0.75 shadow-[inset_0_0_0_1px_var(--color-line-soft)]";

function FilterKey({ option }: { option: FilterOption }) {
  return (
    <ToggleButton
      id={option.id}
      className={cx(
        "flex h-8 cursor-pointer items-center gap-2 rounded-key px-2.75 type-label font-semibold text-ink-2 motion-colors outline-none",
        "hover:text-ink selected:bg-raised selected:text-ink selected:shadow-[0_0_0_1px_var(--color-line)]",
        "focus-visible:outline-2 focus-visible:outline-focus",
      )}
    >
      <span>{option.label}</span>
      {option.count !== undefined ? (
        <span
          className={cx(
            "inline-flex h-5 min-w-5 items-center justify-center rounded-tag px-1 type-numeral motion-colors",
            option.tone === "attention" && option.count > 0 && "bg-attention text-on-attention",
            option.tone === "fail" && option.count > 0 && "bg-fail text-on-fail",
            (option.tone === undefined || option.count === 0) && "text-muted",
          )}
        >
          {option.count}
        </span>
      ) : null}
    </ToggleButton>
  );
}

// The search box of a list. The label is inside the field as a word, so the field stays compact in a header.
export function SearchField({
  label,
  placeholder,
  className,
  ...props
}: Omit<AriaSearchFieldProps, "className"> & {
  label: string;
  placeholder?: string;
  className?: string;
}) {
  return (
    <AriaSearchField
      {...props}
      aria-label={label}
      className={cx("group relative flex w-80 max-w-full", className)}
    >
      <IconSearch
        aria-hidden="true"
        size={16}
        stroke={2}
        className="pointer-events-none absolute top-2.75 left-2.5 text-muted"
      />
      <Input
        {...(placeholder === undefined ? {} : { placeholder })}
        className={cx(fieldClass, "h-9.5 pl-8 type-body [&::-webkit-search-cancel-button]:hidden")}
      />
    </AriaSearchField>
  );
}

// A bar for one quantity, such as an upload. A step of a run uses the sequence rail instead.
export function ProgressBar({
  label,
  value,
  valueLabel,
  className,
}: {
  label: ReactNode;
  // From 0 to 100; undefined while the amount is not known yet.
  value?: number;
  valueLabel?: string;
  className?: string;
}) {
  return (
    <AriaProgressBar
      {...(value === undefined ? { isIndeterminate: true } : { value })}
      {...(valueLabel === undefined ? {} : { valueLabel })}
      className={cx("flex flex-col gap-1.5", className)}
    >
      {({ percentage, valueText, isIndeterminate }) => (
        <>
          <span className="flex justify-between gap-3 type-small">
            <Label className="text-ink-2">{label}</Label>
            {isIndeterminate ? null : <span className="text-ink">{valueText}</span>}
          </span>
          <span className="relative block h-2 overflow-hidden rounded-tag bg-well shadow-[inset_0_0_0_1px_var(--color-rail-edge)]">
            <span
              className={cx(
                "absolute inset-y-0 left-0 bg-run rail-live",
                isIndeterminate ? "w-full opacity-60" : "motion-fill",
              )}
              style={isIndeterminate ? undefined : { width: `${String(percentage ?? 0)}%` }}
            />
          </span>
        </>
      )}
    </AriaProgressBar>
  );
}

// A whole number with a unit, for sizes, timeouts and exit codes. Keys step it; typing checks it on leaving.
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
