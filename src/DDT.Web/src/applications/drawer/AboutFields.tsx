// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { TextField } from "@/ui/TextField";

import { errorsAt } from "./errorsAt";
import type { ApplicationForm } from "./useApplicationForm";

// What the application is: its name, publisher, version and description.
export function AboutFields({ form }: { form: ApplicationForm }) {
  const { edit, findings, change } = form;
  const at = (field: string) => errorsAt(findings, field);

  return (
    <>
      <TextField
        label={<Trans>Name</Trans>}
        value={edit.name}
        onChange={(name) => {
          change({ name });
        }}
        {...at("name")}
      />
      <div className="grid grid-cols-[minmax(0,3fr)_minmax(0,2fr)] gap-3">
        <TextField
          label={<Trans>Publisher</Trans>}
          value={edit.publisher}
          onChange={(publisher) => {
            change({ publisher });
          }}
          {...at("publisher")}
        />
        <TextField
          label={<Trans>Version</Trans>}
          mono
          value={edit.version}
          onChange={(version) => {
            change({ version });
          }}
          {...at("version")}
        />
      </div>
      <TextField
        label={<Trans>Description</Trans>}
        multiline
        rows={2}
        value={edit.description}
        onChange={(description) => {
          change({ description });
        }}
        {...at("description")}
      />
    </>
  );
}
