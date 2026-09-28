// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { UseQueryResult } from "@tanstack/react-query";
import { useState } from "react";

import { FieldErrorText } from "@/ui/FieldErrorText";
import { Notice } from "@/ui/Notice";

import {
  otherEntries,
  withEntries,
  type PxeForm,
  type PxeHostInterfaces,
  type PxeSettings,
} from "../networkBoot";
import { LockNote } from "../parts/LockNote";
import { SettingsGroup } from "../parts/SettingsGroup";

import { AddInterfaceForm } from "./AddInterfaceForm";
import { OtherEntries } from "./OtherEntries";
import { ReportedInterfaces } from "./ReportedInterfaces";
import { RescanButton } from "./RescanButton";

export function InterfacesGroup({
  form,
  values,
  hosts,
}: {
  form: PxeForm;
  values: PxeSettings;
  hosts: UseQueryResult<PxeHostInterfaces[]>;
}) {
  // Kept here instead of in AddInterfaceForm, so the typed text survives a lock that comes and goes.
  const [typed, setTyped] = useState("");
  const entries = values.interfaces;
  const lock = form.lockOf("interfaces");
  const editable = lock === null;
  const errors = form.fieldErrors("interfaces");
  const warnings = (form.view?.warnings ?? []).filter((warning) => warning.field === "interfaces");
  const others = otherEntries(entries, hosts.data ?? []);

  const set = (next: string[]) => {
    form.change("interfaces", next);
  };

  return (
    <SettingsGroup title={<Trans>Interfaces</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          A host may have a network adapter on a segment whose DHCP belongs to someone else, so DDT
          answers on no interface until it is listed. List the interfaces machines reach DDT on, and
          never a VPN tunnel.
        </Trans>
      </p>
      {entries.length === 0 ? (
        <Notice tone="attention">
          <Trans>
            No interface is listed, so DDT answers no machine that netboots. Choose the interfaces
            below, or type one.
          </Trans>
        </Notice>
      ) : null}
      {lock === null ? null : <LockNote lock={lock} />}

      <ReportedInterfaces hosts={hosts} entries={entries} editable={editable} onChange={set} />

      {others.length > 0 ? (
        <OtherEntries
          others={others}
          editable={editable}
          onRemove={(entry) => {
            set(entries.filter((listed) => listed !== entry));
          }}
        />
      ) : null}

      {editable ? (
        <AddInterfaceForm
          typed={typed}
          onType={setTyped}
          onAdd={() => {
            set(withEntries(entries, typed));
            setTyped("");
          }}
        />
      ) : null}

      <FieldErrorText errors={errors} />
      {warnings.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {warnings.map((warning, index) => (
            <li key={index} className="type-small text-attention-text">
              {warning.message}
            </li>
          ))}
        </ul>
      ) : null}

      <RescanButton form={form} />
    </SettingsGroup>
  );
}
