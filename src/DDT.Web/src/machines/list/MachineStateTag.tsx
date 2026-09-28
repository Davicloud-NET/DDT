// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import type { MachineSummary } from "@/machines/machines";
import { machineTag } from "@/machines/machineView";
import { StateTag } from "@/ui/StateTag";

// The machine's state, or that its run waits for someone.
export function MachineStateTag({ machine }: { machine: MachineSummary }) {
  const { i18n } = useLingui();
  const tag = machineTag(machine);

  return <StateTag tone={tag.tone}>{i18n._(tag.label)}</StateTag>;
}
