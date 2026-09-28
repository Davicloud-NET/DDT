// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";

import type { SettingsForm } from "../useSettingsForm";

// Problems of the whole section, such as a missing directory proof, shown next to the refused save.
export function SectionErrors<T>({ form }: { form: SettingsForm<T> }) {
  const errors = form.fieldErrors("");
  const refused = form.refusal?.kind === "invalid" ? form.refusal : null;
  const fieldsRefused = refused !== null && Object.keys(refused.fields).some((key) => key !== "");

  if (refused === null && errors.length === 0) {
    return null;
  }

  return (
    <Notice tone="fail">
      <span className="flex flex-col gap-1.5">
        {refused === null ? null : fieldsRefused ? (
          <span>
            <Trans>Nothing was saved. The fields marked above say why.</Trans>
          </span>
        ) : (
          <span>
            <Trans>Nothing was saved.</Trans>
          </span>
        )}
        {errors.length === 1 ? <span>{errors[0]}</span> : null}
        {errors.length > 1 ? (
          <ul className="flex list-disc flex-col gap-1 pl-5">
            {errors.map((error, index) => (
              <li key={index}>{error}</li>
            ))}
          </ul>
        ) : null}
      </span>
    </Notice>
  );
}
