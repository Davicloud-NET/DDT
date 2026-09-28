// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { RebootStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

// A restart has no settings. It runs in the phase of the step before it.
export function RebootFields({ step }: KindFieldsProps<RebootStep>) {
  const name = step.name;

  return (
    <p className="text-ink-2 sm:col-span-2">
      <Trans>
        Restarts the machine, and the sequence goes on with the step after {name}. In Windows PE
        this needs a partitioned disk, where the run's state is kept.
      </Trans>
    </p>
  );
}
