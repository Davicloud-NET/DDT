// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { formatDuration } from "@/lib/format";
import { formatMac } from "@/machines/machines";

import type { RunHistoryItem } from "../runHistory";

export function machineName(item: RunHistoryItem): string {
  return item.machineName ?? item.machineModel ?? formatMac(item.primaryMac);
}

// What the name leaves out: the model under a computer name, else the maker and the MAC address.
export function machineLine(item: RunHistoryItem): string {
  const mac = formatMac(item.primaryMac);

  if (item.machineName !== null) {
    return item.machineModel ?? mac;
  }

  return item.manufacturer === null ? mac : `${item.manufacturer}, ${mac}`;
}

export function durationLine(run: RunHistoryItem["run"], now: number): string {
  if (run.startedUtc === null) {
    return t`Not started`;
  }

  const end = run.finishedUtc === null ? now : Date.parse(run.finishedUtc);
  const took = formatDuration(end - Date.parse(run.startedUtc));

  return run.finishedUtc === null ? t`Running for ${took}` : t`Took ${took}`;
}
