// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Link } from "@tanstack/react-router";

import { plural } from "@/lib/format";

import { FormCheckbox } from "../FormCheckbox";
import { fieldMessages } from "../problems";
import type { InjectDriversStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

import styles from "../form.module.scss";

export function InjectDriversFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<InjectDriversStep>) {
  const drivers = catalog.packages.filter((item) => item.kind === "Drivers");

  return (
    <>
      <p className={styles.explain}>
        Adds the driver packages whose targets match the machine's model to the applied image. The
        packages are chosen when the sequence is assigned.
      </p>
      <p className={styles.hint}>
        {drivers.length === 0
          ? "No driver package is in the library yet. "
          : `${plural(drivers.length, "driver package")} in the library, each for the models its targets name. `}
        <Link to="/packages">Driver packages and their targets are on the Packages page.</Link>
      </p>
      <FormCheckbox
        label="Fail when no driver package matches the model"
        checked={step.requireMatch}
        messages={fieldMessages(findings, "requireMatch")}
        hint="Otherwise the step is done without adding drivers."
        onChange={(requireMatch) => {
          onChange({ requireMatch });
        }}
      />
    </>
  );
}
