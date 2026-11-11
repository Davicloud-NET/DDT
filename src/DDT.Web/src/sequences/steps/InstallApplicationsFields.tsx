// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useContext } from "react";

import { OrderedPicker } from "@/applications/drawer/OrderedPicker";
import { libraryEntries } from "@/applications/libraryEntries";

import { EditorLock } from "../editorLock";
import { TextSetting } from "../fields/TextSetting";
import type { ApplicationReference, InstallApplicationsStep } from "../sequences";
import { orNull, type KindFieldsProps } from "./kindFields";

// The applications and bundles in the order they install, and the value that names more. One no longer in the
// library stays in the list, marked, until it's removed.
export function InstallApplicationsFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<InstallApplicationsStep>) {
  const locked = useContext(EditorLock);
  const options = libraryEntries(catalog.applications, catalog.bundles);
  const referenceOf = (id: string): ApplicationReference => {
    const known = options.find((option) => option.id === id) ??
      step.applications.find((reference) => reference.id === id) ?? { kind: "Application" };

    return { id, kind: known.kind };
  };

  return (
    <>
      <div className="sm:col-span-2">
        <OrderedPicker
          label={<Trans>Applications and bundles</Trans>}
          field="applications"
          ids={step.applications.map((reference) => reference.id)}
          options={options}
          addLabel={t`Add an application or bundle`}
          findings={findings}
          isReadOnly={locked}
          onChange={(ids) => {
            onChange({ applications: ids.map(referenceOf) }, true);
          }}
        />
      </div>
      <TextSetting
        label={<Trans>Value with more</Trans>}
        field="variable"
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>Names or ids, separated by semicolons. Installed after the list.</Trans>}
        mono
        value={step.variable ?? ""}
        onChange={(variable) => {
          onChange({ variable: orNull(variable) });
        }}
      />
    </>
  );
}
