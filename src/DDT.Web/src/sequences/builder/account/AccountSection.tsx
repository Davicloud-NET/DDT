// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Header, ListBoxSection } from "react-aria-components";

import { ListBoxItem } from "@/ui/Select";

import type { AccountItem } from "./useAccountChoices";

// A section of the account list. It's left out while it's empty.
export function AccountSection({ title, items }: { title: string; items: readonly AccountItem[] }) {
  return items.length > 0 ? (
    <ListBoxSection>
      <Header className="px-2.5 pt-2 pb-1 type-small text-muted">{title}</Header>
      {items.map((item) => (
        <ListBoxItem
          key={item.key}
          id={item.id}
          textValue={item.text}
          {...(item.description === null ? {} : { description: item.description })}
        >
          {item.text}
        </ListBoxItem>
      ))}
    </ListBoxSection>
  ) : null;
}
