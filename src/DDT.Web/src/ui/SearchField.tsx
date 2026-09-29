// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconSearch } from "@tabler/icons-react";
import {
  Input,
  SearchField as AriaSearchField,
  type SearchFieldProps as AriaSearchFieldProps,
} from "react-aria-components";

import { cx } from "./cx";
import { fieldClass } from "./TextField";

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
      className={cx("group relative flex w-80 max-w-full max-sm:w-full", className)}
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
