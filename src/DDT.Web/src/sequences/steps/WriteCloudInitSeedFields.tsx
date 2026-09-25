// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { FormCheckbox } from "../FormCheckbox";
import { FormField } from "../FormField";
import { fieldMessages } from "../problems";
import type { WriteCloudInitSeedStep } from "../sequences";
import { seedPlaceholders } from "../steps";
import type { KindFieldsProps } from "./kindFields";

import styles from "../form.module.scss";

const placeholders = seedPlaceholders.map((name) => `{{${name}}}`).join(", ");

export function WriteCloudInitSeedFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<WriteCloudInitSeedStep>) {
  return (
    <>
      <p className={styles.explain}>
        Adds a partition labelled CIDATA at the end of the disk, where cloud-init finds these files
        at the machine's first start. Every viewer of DDT can read them, so passwords go in hashed.
      </p>
      <p className={styles.hint}>
        {`DDT fills in ${placeholders} with the machine's values. Put them in double quotes, such as hostname: "{{ComputerName}}". Anything else in double braces stays as it is, for cloud-init's own templates.`}
      </p>
      <FormField label="meta-data" messages={fieldMessages(findings, "metaData")}>
        {(control) => (
          <textarea
            {...control}
            className={styles.script}
            spellCheck={false}
            value={step.metaData}
            onChange={(event) => {
              onChange({ metaData: event.target.value });
            }}
          />
        )}
      </FormField>
      <FormField
        label="user-data"
        messages={fieldMessages(findings, "userData")}
        hint="A #cloud-config document, or a script that starts with #!."
      >
        {(control) => (
          <textarea
            {...control}
            className={styles.script}
            spellCheck={false}
            value={step.userData}
            onChange={(event) => {
              onChange({ userData: event.target.value });
            }}
          />
        )}
      </FormField>
      <FormCheckbox
        label="Write network-config"
        checked={step.networkConfig !== null}
        messages={step.networkConfig === null ? fieldMessages(findings, "networkConfig") : []}
        hint="Without it, the image configures its network itself, usually by DHCP."
        onChange={(write) => {
          onChange({ networkConfig: write ? "version: 2\n" : null });
        }}
      />
      {step.networkConfig !== null && (
        <FormField label="network-config" messages={fieldMessages(findings, "networkConfig")}>
          {(control) => (
            <textarea
              {...control}
              className={styles.script}
              spellCheck={false}
              value={step.networkConfig ?? ""}
              onChange={(event) => {
                onChange({ networkConfig: event.target.value });
              }}
            />
          )}
        </FormField>
      )}
    </>
  );
}
