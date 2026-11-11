// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlagSetting } from "../fields/FlagSetting";
import type { InjectDriversStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

export function InjectDriversFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<InjectDriversStep>) {
  const count = catalog.packages.filter((item) => item.kind === "Drivers").length;

  return (
    <>
      {count === 0 ? (
        <p className="type-small text-ink-2 sm:col-span-2">
          <Trans>No driver package is in the library yet.</Trans>
        </p>
      ) : null}
      <FlagSetting
        label={<Trans>Fail when no driver package matches the model</Trans>}
        field="requireMatch"
        findings={findings}
        className="sm:col-span-2"
        value={step.requireMatch}
        onChange={(requireMatch) => {
          onChange({ requireMatch });
        }}
      />
    </>
  );
}
