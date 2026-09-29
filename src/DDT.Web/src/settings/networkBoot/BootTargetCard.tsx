// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useId } from "react";

import { FieldErrorText } from "@/ui/FieldErrorText";

import {
  canonicalArchitecture,
  methodFor,
  type BootTargetSettings,
  type PxeConfiguration,
  type PxeForm,
  type PxeHostInterfaces,
} from "../networkBoot";
import { SettingSwitch } from "../parts/SettingSwitch";

import { BootFileField } from "./BootFileField";
import { BootServerFields } from "./BootServerFields";
import { targetErrors, targetForm } from "./bootTargetForm";
import { BootTargetHeading } from "./BootTargetHeading";
import { withoutTarget } from "./bootTargets";
import { TargetMethod } from "./TargetMethod";

interface BootTargetCardProps {
  form: PxeForm;
  targetKey: string;
  target: BootTargetSettings | null;
  editable: boolean;
  hosts: PxeHostInterfaces[];
  configuration: PxeConfiguration | null;
}

// One architecture's boot target, stored as bootTargets.<key> in the section.
export function BootTargetCard({
  form,
  targetKey,
  target,
  editable,
  hosts,
  configuration,
}: BootTargetCardProps) {
  const headingId = useId();
  const fields = targetForm(form);
  const key = targetKey;
  const canonical = canonicalArchitecture(key);
  const method = canonical === null ? null : methodFor(canonical);
  const path = (field: string) => `bootTargets.${key}.${field}`;

  return (
    <div
      role="group"
      aria-labelledby={headingId}
      className="flex flex-col gap-3 rounded-key p-3.5 shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
    >
      <BootTargetHeading
        headingId={headingId}
        targetKey={key}
        editable={editable}
        onRemove={() => {
          form.change("bootTargets", withoutTarget(form.values?.bootTargets ?? {}, key));
        }}
      />

      <FieldErrorText errors={targetErrors(form, key, "")} />

      {method === null ? null : (
        <TargetMethod
          method={method}
          stored={target?.method ?? null}
          errors={targetErrors(form, key, "method")}
          editable={editable}
          onUse={() => {
            form.change(path("method"), method);
          }}
        />
      )}

      <div className="grid gap-4 md:grid-cols-2">
        <BootFileField
          form={fields}
          field={path("bootFile")}
          http={method === "Http"}
          editable={editable}
          hosts={hosts}
          configuration={configuration}
        />
        <BootServerFields form={fields} path={path} http={method === "Http"} editable={editable} />
      </div>
      <SettingSwitch
        form={fields}
        field={path("advertiseBootServerDiscovery")}
        canChange={editable}
        label={<Trans>Advertise boot server discovery</Trans>}
        hint={
          <Trans>
            Only ever useful for BIOS machines: UEFI firmware can refuse an answer that carries it.
          </Trans>
        }
      />
    </div>
  );
}
