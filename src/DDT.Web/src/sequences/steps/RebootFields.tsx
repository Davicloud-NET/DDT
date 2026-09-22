// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { RebootStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

import styles from "../form.module.scss";

// A restart has no settings; it runs in the phase of the step before it.
export function RebootFields({ step }: KindFieldsProps<RebootStep>) {
  return (
    <p className={styles.explain}>
      {`Restarts the machine, and the sequence goes on with the step after ${step.name}. In Windows PE this needs a partitioned disk, where the run's state is kept.`}
    </p>
  );
}
