// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { TemplateField } from "../builder/TemplateField";
import type { WriteUnattendStep } from "../sequences";
import { orNull, type KindFieldsProps } from "./kindFields";

// The organization and owner Windows shows as registered.
export function RegisteredFields({
  step,
  findings,
  onChange,
}: Pick<KindFieldsProps<WriteUnattendStep>, "step" | "findings" | "onChange">) {
  const serverDefault = t`Server default`;

  return (
    <>
      <TemplateField
        label={<Trans>Organization</Trans>}
        field="organization"
        mono={false}
        howTo={false}
        findings={findings}
        placeholder={serverDefault}
        value={step.organization ?? ""}
        onChange={(text) => {
          onChange({ organization: orNull(text) });
        }}
      />
      <TemplateField
        label={<Trans>Owner</Trans>}
        field="owner"
        mono={false}
        howTo={false}
        findings={findings}
        placeholder={serverDefault}
        value={step.owner ?? ""}
        onChange={(text) => {
          onChange({ owner: orNull(text) });
        }}
      />
    </>
  );
}
