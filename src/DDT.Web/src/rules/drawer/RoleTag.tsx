// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { cx } from "@/ui/cx";

interface RoleTagProps {
  roleName: string;
  // Where the server puts a problem of this role, such as roleIds[0].
  field: string;
  hasProblem: boolean;
  // Read only, without the key that takes the role out.
  locked: boolean;
  onRemove: () => void;
}

// A machine role a rule gives, as a tag with a key that takes it out.
export function RoleTag({ roleName, field, hasProblem, locked, onRemove }: RoleTagProps) {
  const { t } = useLingui();

  return (
    <span
      data-field={field}
      className={cx(
        "inline-flex h-7 items-center gap-1 rounded-key pr-1 pl-2.5 type-body text-ink",
        hasProblem
          ? "shadow-[inset_0_0_0_1.5px_var(--color-fail-text)]"
          : "shadow-[inset_0_0_0_1px_var(--color-line)]",
        locked && "pr-2.5",
      )}
    >
      {roleName}
      {locked ? null : (
        <AriaButton
          aria-label={t`Take ${roleName} out`}
          className="flex size-5 cursor-pointer items-center justify-center rounded-tag text-muted key-motion outline-none hover:bg-hover hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
          onPress={onRemove}
        >
          <IconX size={12} stroke={2} />
        </AriaButton>
      )}
    </span>
  );
}
