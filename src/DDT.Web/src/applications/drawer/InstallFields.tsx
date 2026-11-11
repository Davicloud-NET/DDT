// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { fieldFindings } from "@/sequences/problems";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import type { ApplicationKind } from "../applications";
import { applicationKinds, kindLabel } from "../applicationView";
import { errorsAt } from "./errorsAt";
import { FilesField } from "./FilesField";
import type { ApplicationForm } from "./useApplicationForm";

// How the application installs: files and a command, an MSIX package, or a winget package.
export function InstallFields({ form }: { form: ApplicationForm }) {
  const { t } = useLingui();
  const { edit, findings, change } = form;
  const at = (field: string) => errorsAt(findings, field);

  return (
    <>
      <div data-field="kind">
        <Select
          label={<Trans>Kind</Trans>}
          value={edit.kind}
          onChange={(key) => {
            if (key !== null) {
              change({ kind: String(key) as ApplicationKind });
            }
          }}
          {...at("kind")}
        >
          {applicationKinds.map((kind) => (
            <ListBoxItem key={kind} id={kind}>
              {kindLabel(kind)}
            </ListBoxItem>
          ))}
        </Select>
      </div>

      {edit.kind === "Winget" ? (
        <div className="grid grid-cols-[minmax(0,3fr)_minmax(0,2fr)] gap-3">
          <TextField
            label={<Trans>winget ID</Trans>}
            mono
            autoComplete="off"
            spellCheck="false"
            placeholder="Mozilla.Firefox"
            value={edit.wingetId}
            onChange={(wingetId) => {
              change({ wingetId });
            }}
            {...at("wingetId")}
          />
          <TextField
            label={<Trans>Version</Trans>}
            mono
            autoComplete="off"
            spellCheck="false"
            placeholder={t`Latest`}
            value={edit.wingetVersion}
            onChange={(wingetVersion) => {
              change({ wingetVersion });
            }}
            {...at("wingetVersion")}
          />
        </div>
      ) : (
        <FilesField
          files={edit.files}
          isRequired={edit.kind === "Msix"}
          errors={fieldFindings(findings, "packageId").problems}
          onChange={(files) => {
            change({ files });
          }}
        />
      )}

      {edit.kind === "Command" ? (
        <TextField
          label={<Trans>Command</Trans>}
          mono
          autoComplete="off"
          spellCheck="false"
          value={edit.command}
          onChange={(command) => {
            change({ command });
          }}
          {...at("command")}
        />
      ) : null}
    </>
  );
}
