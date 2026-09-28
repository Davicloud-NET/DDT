// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useState } from "react";

import { ComboBox, ListBoxItem } from "@/ui/Select";

import {
  hostOf,
  type PxeConfiguration,
  type PxeForm,
  type PxeHostInterfaces,
} from "../networkBoot";
import { valueAt } from "../valuePath";

import { BootFileHint } from "./BootFileHint";
import { bootFileOptions, serverNameOptions } from "./bootTargets";
import { BootUrlServerField } from "./BootUrlServerField";

interface BootFileFieldProps {
  form: PxeForm;
  field: string;
  http: boolean;
  editable: boolean;
  hosts: PxeHostInterfaces[];
  configuration: PxeConfiguration | null;
}

// The boot file, with the two boot managers of the boot image layout to pick from and any other path or URL typed.
// For HTTP, the URLs are built from a server name, the boot port and /boot/.
export function BootFileField({
  form,
  field,
  http,
  editable,
  hosts,
  configuration,
}: BootFileFieldProps) {
  const { t } = useLingui();
  const stored = valueAt(form.values, field);
  const value = typeof stored === "string" ? stored : "";
  const errors = form.fieldErrors(field);
  const port = configuration?.httpBootPort ?? null;
  const here = window.location.hostname;
  const [server, setServer] = useState(() => hostOf(value) ?? here);
  const name = server.trim() === "" ? here : server.trim();

  return (
    <>
      {http ? (
        <BootUrlServerField
          server={server}
          onChange={setServer}
          options={serverNameOptions(here, hosts)}
          editable={editable}
        />
      ) : null}
      <ComboBox
        label={<Trans>Boot file</Trans>}
        hint={<BootFileHint http={http} port={port} />}
        mono
        allowsCustomValue
        inputValue={value}
        onInputChange={(next) => {
          form.change(field, next.trim() === "" ? null : next);
        }}
        isReadOnly={!editable}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
      >
        {bootFileOptions(http, name, port).map((option) => (
          <ListBoxItem
            key={option.file}
            id={option.file}
            textValue={option.file}
            description={
              option.authority === "2011"
                ? t`Microsoft 2011 CA. The default, which most machines trust.`
                : t`Windows UEFI CA 2023.`
            }
          >
            {option.file}
          </ListBoxItem>
        ))}
      </ComboBox>
    </>
  );
}
