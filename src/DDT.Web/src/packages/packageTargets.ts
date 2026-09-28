// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { HardwareModel, HardwareModelCount } from "@/machines/machines";

// A hardware model as the package dialog edits it. key tells the rows apart while their text changes.
export interface TargetRow {
  key: number;
  manufacturer: string;
  model: string;
}

export function targetRows(targets: readonly HardwareModel[]): TargetRow[] {
  return targets.map((target, index) => ({
    key: index,
    manufacturer: target.manufacturer ?? "",
    model: target.model,
  }));
}

// The targets a save sends: rows without a model are left out, and an empty manufacturer stands for any.
export function cleanTargets(rows: readonly TargetRow[]): HardwareModel[] {
  return rows
    .filter((target) => target.model.trim() !== "")
    .map((target) => ({
      manufacturer: target.manufacturer.trim() === "" ? null : target.manufacturer.trim(),
      model: target.model.trim(),
    }));
}

export function manufacturersOf(models: readonly HardwareModelCount[]): string[] {
  return [
    ...new Set(
      models.flatMap((model) => (model.manufacturer === null ? [] : [model.manufacturer])),
    ),
  ];
}

// The models to offer for a manufacturer; every model while it is empty.
export function modelsOf(
  models: readonly HardwareModelCount[],
  manufacturer: string,
): HardwareModelCount[] {
  return models.filter(
    (model) => manufacturer.trim() === "" || model.manufacturer === manufacturer.trim(),
  );
}
