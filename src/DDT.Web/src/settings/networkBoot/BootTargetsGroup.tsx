// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useState } from "react";

import { FieldErrorText } from "@/ui/FieldErrorText";

import type { PxeConfiguration, PxeForm, PxeHostInterfaces, PxeSettings } from "../networkBoot";
import { LockNote } from "../parts/LockNote";
import { SettingsGroup } from "../parts/SettingsGroup";

import { AddBootTarget } from "./AddBootTarget";
import { BootTargetCard } from "./BootTargetCard";
import { architecturesWithout, newBootTarget, targetKeysInOrder } from "./bootTargets";

export function BootTargetsGroup({
  form,
  values,
  hosts,
  configuration,
}: {
  form: PxeForm;
  values: PxeSettings;
  hosts: PxeHostInterfaces[];
  configuration: PxeConfiguration | null;
}) {
  const targets = values.bootTargets;
  const keys = targetKeysInOrder(targets);
  const lock = form.lockOf("bootTargets");
  const editable = lock === null;
  const available = architecturesWithout(keys);
  // Kept here instead of in AddBootTarget, so the choice survives a lock that comes and goes.
  const [chosen, setChosen] = useState<string | null>(null);
  const adding = chosen !== null && available.includes(chosen) ? chosen : (available[0] ?? null);
  const errors = form.fieldErrors("bootTargets");

  const add = () => {
    if (adding === null) {
      return;
    }

    const port = configuration?.httpBootPort ?? null;

    form.change("bootTargets", {
      ...targets,
      [adding]: newBootTarget(adding, window.location.hostname, port),
    });
    setChosen(null);
  };

  return (
    <SettingsGroup title={<Trans>Boot targets</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          ProxyDHCP sends a machine the boot file of the architecture its firmware reports, and
          answers no architecture without a boot target. A site whose own DHCP server names DDT and
          the boot file needs none.
        </Trans>
      </p>
      {lock === null ? null : <LockNote lock={lock} />}
      <FieldErrorText errors={errors} />

      {keys.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>No boot target yet, so ProxyDHCP answers no machine.</Trans>
        </p>
      ) : (
        keys.map((key) => (
          <BootTargetCard
            key={key}
            form={form}
            targetKey={key}
            target={targets[key] ?? null}
            editable={editable}
            hosts={hosts}
            configuration={configuration}
          />
        ))
      )}

      {editable && adding !== null ? (
        <AddBootTarget
          architecture={adding}
          available={available}
          onChoose={setChosen}
          onAdd={add}
        />
      ) : null}
    </SettingsGroup>
  );
}
