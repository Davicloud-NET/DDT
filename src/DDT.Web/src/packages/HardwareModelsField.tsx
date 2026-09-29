// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";

import type { HardwareModel, HardwareModelCount } from "@/machines/machines";
import { Button } from "@/ui/Button";
import { FieldErrorText } from "@/ui/FieldErrorText";

import { HardwareModelRow } from "./HardwareModelRow";
import { matchingMachines } from "./packages";
import { manufacturersOf, modelsOf } from "./packageTargets";
import type { TargetRows } from "./useTargetRows";

interface HardwareModelsFieldProps {
  targets: TargetRows;
  // The rows as a save sends them.
  cleaned: readonly HardwareModel[];
  // The models registered machines reported, for the suggestions and the match count.
  models: readonly HardwareModelCount[];
  errors: readonly string[];
}

// The hardware models a driver package is for, with how many registered machines match them.
export function HardwareModelsField({
  targets,
  cleaned,
  models,
  errors,
}: HardwareModelsFieldProps) {
  const { t } = useLingui();
  const manufacturers = manufacturersOf(models);
  const matching = matchingMachines(cleaned, models);

  return (
    <fieldset className="flex flex-col gap-2.5">
      <legend className="type-label text-ink">
        <Trans>For these hardware models</Trans>
      </legend>
      <p className="type-small text-muted">
        <Trans>
          An Inject drivers step adds these drivers to machines whose model matches one of them. End
          a model with * to match every model that starts with it; leave the manufacturer empty for
          any.
        </Trans>
      </p>
      {targets.rows.map((target, index) => (
        <HardwareModelRow
          key={target.key}
          row={target}
          position={index + 1}
          manufacturers={manufacturers}
          models={modelsOf(models, target.manufacturer)}
          onChange={(change) => {
            targets.change(target.key, change);
          }}
          onRemove={() => {
            targets.remove(target.key);
          }}
        />
      ))}
      <span className="flex flex-wrap items-center gap-3">
        <Button size="sm" onPress={targets.add}>
          <IconPlus size={14} stroke={2} aria-hidden="true" />
          <Trans>Add a model</Trans>
        </Button>
        <span className="type-small text-muted">
          {cleaned.length === 0
            ? t`No model yet: no machine gets these drivers.`
            : matching === 0
              ? t`No registered machine matches yet.`
              : plural(matching, {
                  one: "Matches # registered machine.",
                  other: "Matches # registered machines.",
                })}
        </span>
      </span>
      <FieldErrorText errors={errors} />
    </fieldset>
  );
}
