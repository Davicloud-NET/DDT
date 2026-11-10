// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { ChoiceSetting } from "../fields/ChoiceSetting";
import type { Choice } from "../fields/fieldBase";
import type { RunScriptStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

// The Select needs a key for "no package". A package id is a GUID, so it can never be this.
const NO_PACKAGE = "none";

// The Files package a script runs in, from the library's Files packages. The Linux boot environment has no place
// for one, so RunScriptFields only shows this there while the step still has a package, to remove it.
export function ScriptPackageSetting({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<RunScriptStep>) {
  const files = catalog.packages.filter((item) => item.kind === "Files");
  const listed = step.packageId === null || files.some((item) => item.id === step.packageId);
  const packages: Choice[] = [
    { id: NO_PACKAGE, label: t`No package` },
    ...(listed || step.packageId === null
      ? []
      : [{ id: step.packageId, label: t`Package deleted` }]),
    ...files.map((item) => ({ id: item.id, label: item.name })),
  ];

  return (
    <ChoiceSetting
      label={<Trans>Files package</Trans>}
      field="packageId"
      findings={findings}
      className="sm:col-span-2"
      hint={<Trans>Unpacked before the script runs, and the script's working directory.</Trans>}
      value={step.packageId ?? NO_PACKAGE}
      choices={packages}
      onChange={(packageId) => {
        onChange({ packageId: packageId === NO_PACKAGE ? null : packageId });
      }}
    />
  );
}
