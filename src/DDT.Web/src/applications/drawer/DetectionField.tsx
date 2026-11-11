// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { Findings } from "@/sequences/problems";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import type { DetectionEdit, DetectionKind } from "../applicationEdit";
import { detectionKinds, detectionLabel } from "../applicationView";
import { errorsAt } from "./errorsAt";

interface DetectionFieldProps {
  detection: DetectionEdit;
  findings: Findings;
  onChange: (detection: DetectionEdit) => void;
}

const textProps = { mono: true, autoComplete: "off", spellCheck: "false" } as const;

// How the agent tells the application is installed already, so it skips it.
export function DetectionField({ detection, findings, onChange }: DetectionFieldProps) {
  const { t } = useLingui();
  const at = (field: string) => errorsAt(findings, `detection.${field}`);
  const set = (patch: Partial<DetectionEdit>) => {
    onChange({ ...detection, ...patch });
  };

  return (
    <div data-field="detection" className="flex flex-col gap-3">
      <Select
        label={<Trans>Detection</Trans>}
        value={detection.kind}
        onChange={(key) => {
          if (key !== null) {
            set({ kind: String(key) as DetectionKind });
          }
        }}
        {...errorsAt(findings, "detection")}
      >
        {detectionKinds.map((kind) => (
          <ListBoxItem
            key={kind}
            id={kind}
            {...(kind === "none" ? { description: t`Installs every time` } : {})}
          >
            {detectionLabel(kind)}
          </ListBoxItem>
        ))}
      </Select>

      {detection.kind === "productCode" ? (
        <TextField
          label={<Trans>Product code</Trans>}
          {...textProps}
          placeholder="{00000000-0000-0000-0000-000000000000}"
          value={detection.productCode}
          onChange={(productCode) => {
            set({ productCode });
          }}
          {...at("productCode")}
        />
      ) : null}

      {detection.kind === "uninstallKey" ? (
        <TextField
          label={<Trans>Key name</Trans>}
          hint={<Trans>Under HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall</Trans>}
          {...textProps}
          value={detection.keyName}
          onChange={(keyName) => {
            set({ keyName });
          }}
          {...at("keyName")}
        />
      ) : null}

      {detection.kind === "file" ? (
        <div className="grid grid-cols-[minmax(0,3fr)_minmax(0,2fr)] gap-3">
          <TextField
            label={<Trans>Path</Trans>}
            {...textProps}
            placeholder="%ProgramFiles%\App\app.exe"
            value={detection.path}
            onChange={(path) => {
              set({ path });
            }}
            {...at("path")}
          />
          <TextField
            label={<Trans>Minimum version</Trans>}
            {...textProps}
            placeholder="1.2.3.4"
            value={detection.minimumVersion}
            onChange={(minimumVersion) => {
              set({ minimumVersion });
            }}
            {...at("minimumVersion")}
          />
        </div>
      ) : null}
    </div>
  );
}
