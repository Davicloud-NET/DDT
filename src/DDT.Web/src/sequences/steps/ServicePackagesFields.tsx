// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useContext } from "react";

import { OrderedPicker } from "@/applications/drawer/OrderedPicker";
import { windowsContentLabel } from "@/packages/windowsPackageView";

import { EditorLock } from "../editorLock";
import { FlagSetting } from "../fields/FlagSetting";
import type { ServicePackagesStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

// The Windows packages the step adds, from the library's Windows packages. DISM adds language packs first, then
// features, then updates, so the order only counts within each.
export function ServicePackagesFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<ServicePackagesStep>) {
  const locked = useContext(EditorLock);
  const options = catalog.packages
    .filter((item) => item.kind === "WindowsPackage")
    .map((item) => ({
      id: item.id,
      name: item.name,
      note:
        item.windows === null || item.windows === undefined
          ? ""
          : windowsContentLabel(item.windows.content),
    }));

  return (
    <>
      <div className="sm:col-span-2">
        <OrderedPicker
          label={<Trans>Windows packages</Trans>}
          field="packages"
          ids={step.packages}
          options={options}
          addLabel={t`Add a Windows package`}
          findings={findings}
          isReadOnly={locked}
          onChange={(packages) => {
            onChange({ packages }, true);
          }}
        />
      </div>
      <FlagSetting
        label={<Trans>Add the updates marked for the image's build</Trans>}
        field="matchingUpdates"
        findings={findings}
        className="sm:col-span-2"
        value={step.matchingUpdates}
        onChange={(matchingUpdates) => {
          onChange({ matchingUpdates });
        }}
      />
      <FlagSetting
        label={<Trans>Clean up after the updates</Trans>}
        field="cleanup"
        findings={findings}
        className="sm:col-span-2"
        value={step.cleanup}
        onChange={(cleanup) => {
          onChange({ cleanup });
        }}
      />
    </>
  );
}
