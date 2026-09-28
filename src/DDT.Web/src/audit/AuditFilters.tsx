// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { SearchField } from "@/ui/SearchField";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { actionGroups } from "./auditView";
import type { AuditFilterFields } from "./useAuditFilter";

export function AuditFilters({ fields }: { fields: AuditFilterFields }) {
  const { i18n, t } = useLingui();

  return (
    <div className="flex flex-wrap items-end gap-3">
      <Select
        label={<Trans>What</Trans>}
        value={fields.action === "" ? "all" : fields.action}
        onChange={(key) => {
          fields.setAction(key === null || key === "all" ? "" : String(key));
        }}
        className="w-56"
      >
        {actionGroups.map((group) => (
          <ListBoxItem
            key={group.prefix}
            id={group.prefix === "" ? "all" : group.prefix}
            textValue={i18n._(group.label)}
          >
            {i18n._(group.label)}
          </ListBoxItem>
        ))}
      </Select>
      <SearchField
        label={t`Who`}
        placeholder={t`Any part of a name`}
        value={fields.typedActor}
        onChange={fields.setTypedActor}
        className="w-64"
      />
      <TextField
        label={<Trans>From</Trans>}
        type="date"
        value={fields.from}
        onChange={fields.setFrom}
        className="w-44"
      />
      <TextField
        label={<Trans>Until</Trans>}
        type="date"
        value={fields.to}
        onChange={fields.setTo}
        className="w-44"
      />
      {fields.filtered ? (
        <Button variant="quiet" onPress={fields.clear}>
          <Trans>Show everything</Trans>
        </Button>
      ) : null}
    </div>
  );
}
