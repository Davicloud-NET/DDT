// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconCheck, IconChevronDown } from "@tabler/icons-react";
import type { ReactNode } from "react";
import {
  Button as AriaButton,
  ComboBox as AriaComboBox,
  FieldError,
  Input,
  Label,
  ListBox,
  ListBoxItem as AriaListBoxItem,
  Popover,
  Select as AriaSelect,
  SelectValue,
  Text,
  type ComboBoxProps as AriaComboBoxProps,
  type ListBoxItemProps as AriaListBoxItemProps,
  type SelectProps as AriaSelectProps,
} from "react-aria-components";

import { cx } from "./cx";
import { fieldClass } from "./TextField";

// Choosing one value from a list. Select for a closed list, ComboBox when typing narrows a long one (models,
// MAC addresses). Both take their options as ListBoxItem children.

const popoverClass =
  "w-(--trigger-width) min-w-48 rounded-overlay bg-raised p-1 shadow-overlay outline-none entering:animate-pop-in exiting:animate-pop-out";

export function ListBoxItem({
  children,
  className,
  description,
  ...props
}: Omit<AriaListBoxItemProps, "className" | "children"> & {
  children: ReactNode;
  description?: ReactNode;
  className?: string;
}) {
  const text = typeof children === "string" ? children : undefined;

  return (
    <AriaListBoxItem
      {...props}
      {...(props.textValue === undefined && text !== undefined ? { textValue: text } : {})}
      className={cx(
        "flex cursor-pointer items-start gap-2 rounded-key px-2.5 py-2 type-body text-ink motion-highlight outline-none",
        "focused:bg-hover selected:font-semibold disabled:cursor-default disabled:text-muted",
        className,
      )}
    >
      {({ isSelected }) => (
        <>
          <span className="mt-0.5 flex w-4 shrink-0 justify-center" aria-hidden="true">
            {isSelected ? <IconCheck size={14} stroke={2.5} /> : null}
          </span>
          <span className="flex min-w-0 flex-col">
            <span className="truncate">{children}</span>
            {description ? (
              <Text slot="description" className="type-small font-normal text-muted">
                {description}
              </Text>
            ) : null}
          </span>
        </>
      )}
    </AriaListBoxItem>
  );
}

interface FieldParts {
  label: ReactNode;
  hint?: ReactNode;
  errorMessage?: ReactNode;
  className?: string;
}

export function Select<T extends object>({
  label,
  hint,
  errorMessage,
  className,
  children,
  placeholder,
  ...props
}: Omit<AriaSelectProps<T>, "className" | "children"> &
  FieldParts & { children: ReactNode; placeholder?: string }) {
  return (
    <AriaSelect
      {...props}
      {...(placeholder === undefined ? {} : { placeholder })}
      className={cx("flex flex-col gap-1.5", className)}
    >
      <Label className="type-label text-ink">{label}</Label>
      <AriaButton
        className={cx(
          fieldClass,
          "flex h-9.5 cursor-pointer items-center gap-2 text-left type-body",
        )}
      >
        {/* The chosen option's text alone: its description belongs in the open list, not in the field. */}
        <SelectValue className="flex-1 truncate placeholder-shown:text-placeholder">
          {({ defaultChildren, isPlaceholder, selectedText }) =>
            isPlaceholder || selectedText === "" ? defaultChildren : selectedText
          }
        </SelectValue>
        <IconChevronDown aria-hidden="true" size={16} stroke={2} className="shrink-0 text-muted" />
      </AriaButton>
      {hint ? (
        <Text slot="description" className="type-small text-muted">
          {hint}
        </Text>
      ) : null}
      <FieldError className="type-small text-fail-text">{errorMessage}</FieldError>
      <Popover offset={4} className={popoverClass}>
        <ListBox className="max-h-80 overflow-auto outline-none">{children}</ListBox>
      </Popover>
    </AriaSelect>
  );
}

export function ComboBox<T extends object>({
  label,
  hint,
  errorMessage,
  className,
  children,
  placeholder,
  mono = false,
  ...props
}: Omit<AriaComboBoxProps<T>, "className" | "children"> &
  FieldParts & { children: ReactNode; placeholder?: string; mono?: boolean }) {
  return (
    <AriaComboBox {...props} className={cx("flex flex-col gap-1.5", className)}>
      <Label className="type-label text-ink">{label}</Label>
      <div className="relative">
        <Input
          {...(placeholder === undefined ? {} : { placeholder })}
          className={cx(fieldClass, "h-9.5 pr-9", mono ? "type-data" : "type-body")}
        />
        <AriaButton className="absolute inset-y-0 right-0 flex w-9 cursor-pointer items-center justify-center text-muted motion-colors outline-none hover:text-ink">
          <IconChevronDown aria-hidden="true" size={16} stroke={2} />
        </AriaButton>
      </div>
      {hint ? (
        <Text slot="description" className="type-small text-muted">
          {hint}
        </Text>
      ) : null}
      <FieldError className="type-small text-fail-text">{errorMessage}</FieldError>
      <Popover offset={4} className={popoverClass}>
        <ListBox className="max-h-80 overflow-auto outline-none">{children}</ListBox>
      </Popover>
    </AriaComboBox>
  );
}
