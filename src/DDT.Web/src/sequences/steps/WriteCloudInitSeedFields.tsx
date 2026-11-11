// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useRef } from "react";

import { FlagSetting } from "../fields/FlagSetting";
import { TextSetting } from "../fields/TextSetting";
import type { WriteCloudInitSeedStep } from "../sequences";
import { seedPlaceholders } from "../steps";
import type { KindFieldsProps } from "./kindFields";

const placeholders = seedPlaceholders.map((name) => `{{${name}}}`).join(", ");

// The seed cloud-init reads at a raw image's first start.
export function WriteCloudInitSeedFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<WriteCloudInitSeedStep>) {
  // What network-config held when it was turned off. It's kept while this step is shown.
  const lastNetworkConfig = useRef("version: 2\n");
  const example = 'hostname: "{{ComputerName}}"';

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>Everyone who can sign in can read them, so passwords go in hashed.</Trans>
      </p>
      <p className="type-small text-ink-2 sm:col-span-2">
        <Trans>
          DDT fills in <span className="type-data">{placeholders}</span> and any value of the run by
          its name. Put them in double quotes, such as <span className="type-data">{example}</span>.
          Anything else in double braces stays as it is.
        </Trans>
      </p>
      <TextSetting
        label="meta-data"
        field="metaData"
        findings={findings}
        className="sm:col-span-2"
        mono
        multiline
        rows={4}
        value={step.metaData}
        onChange={(metaData) => {
          onChange({ metaData });
        }}
      />
      <TextSetting
        label="user-data"
        field="userData"
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>A #cloud-config document, or a script that starts with #!.</Trans>}
        mono
        multiline
        rows={10}
        value={step.userData}
        onChange={(userData) => {
          onChange({ userData });
        }}
      />
      <FlagSetting
        label={<Trans>Write network-config</Trans>}
        field={step.networkConfig === null ? "networkConfig" : "networkConfig.switch"}
        findings={findings}
        className="sm:col-span-2"
        value={step.networkConfig !== null}
        onChange={(write) => {
          if (!write && step.networkConfig !== null) {
            lastNetworkConfig.current = step.networkConfig;
          }

          onChange({ networkConfig: write ? lastNetworkConfig.current : null }, true);
        }}
      />
      {step.networkConfig !== null ? (
        <TextSetting
          label="network-config"
          field="networkConfig"
          findings={findings}
          className="sm:col-span-2"
          mono
          multiline
          rows={6}
          value={step.networkConfig}
          onChange={(networkConfig) => {
            onChange({ networkConfig });
          }}
        />
      ) : null}
    </>
  );
}
