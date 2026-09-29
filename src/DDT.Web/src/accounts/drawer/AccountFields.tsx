// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { fieldFindings } from "@/sequences/problems";
import { Checkbox } from "@/ui/Checkbox";
import { TextField } from "@/ui/TextField";

import { HostsEditor } from "./HostsEditor";
import type { AccountForm } from "./useAccountForm";

// An account's name, its user name and where it may be used. The password has a separate field.
export function AccountFields({ form }: { form: AccountForm }) {
  const { edit, findings, change } = form;
  const errors = (field: string) => fieldFindings(findings, field).problems;

  return (
    <>
      <TextField
        label={<Trans>Name</Trans>}
        hint={<Trans>How steps and this page name it, such as Join account.</Trans>}
        value={edit.name}
        onChange={(text) => {
          change({ name: text });
        }}
        isInvalid={errors("name").length > 0}
        errorMessage={errors("name").join(" ")}
      />

      <TextField
        label={<Trans>User name</Trans>}
        hint={<Trans>With its domain, as DOMAIN\user or user@corp.example.</Trans>}
        mono
        autoComplete="off"
        spellCheck="false"
        value={edit.userName}
        onChange={(text) => {
          change({ userName: text });
        }}
        isInvalid={errors("userName").length > 0}
        errorMessage={errors("userName").join(" ")}
      />

      <TextField
        label={<Trans>Domain</Trans>}
        hint={
          <Trans>
            Optional. The domain a Join the domain step may join with it, such as corp.example.
          </Trans>
        }
        mono
        autoComplete="off"
        spellCheck="false"
        value={edit.domain}
        onChange={(text) => {
          change({ domain: text });
        }}
        isInvalid={errors("domain").length > 0}
        errorMessage={errors("domain").join(" ")}
      />

      <HostsEditor
        rows={edit.hosts}
        findings={findings}
        onChange={(hosts) => {
          change({ hosts });
        }}
      />

      <div data-field="runAs">
        <Checkbox
          isSelected={edit.runAs}
          onChange={(runAs) => {
            change({ runAs });
          }}
        >
          <span className="flex flex-col">
            <span>
              <Trans>Scripts may run as this account</Trans>
            </span>
            <span className="type-small text-muted">
              <Trans>
                In Windows, after the image is applied. A script in Windows PE runs as the system.
              </Trans>
            </span>
          </span>
        </Checkbox>
      </div>
    </>
  );
}
