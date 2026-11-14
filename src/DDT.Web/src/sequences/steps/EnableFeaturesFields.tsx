// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useContext, useId, useState } from "react";

import { PickedRow } from "@/applications/drawer/PickedRow";
import { Button } from "@/ui/Button";
import { FieldErrorText } from "@/ui/FieldErrorText";
import { ComboBox, ListBoxItem } from "@/ui/Select";

import { EditorLock } from "../editorLock";
import { fieldFindings } from "../problems";
import type { EnableFeaturesStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";
import { KNOWN_FEATURES } from "./knownFeatures";

const sameName = (first: string, second: string) => first.toLowerCase() === second.toLowerCase();

// The features in the order DISM enables them. The list offers common ones and takes any other name.
export function EnableFeaturesFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<EnableFeaturesStep>) {
  const { i18n, t } = useLingui();
  const locked = useContext(EditorLock);
  const labelId = useId();
  const [typed, setTyped] = useState("");
  const features = step.features;
  const offered = KNOWN_FEATURES.filter(
    (known) => !features.some((name) => sameName(name, known.name)),
  );
  const labelOf = (name: string) => {
    const known = KNOWN_FEATURES.find((feature) => sameName(feature.name, name));

    return known === undefined ? null : i18n._(known.label);
  };

  const change = (next: string[]) => {
    onChange({ features: next }, true);
  };

  const add = (name: string) => {
    const trimmed = name.trim();

    if (trimmed !== "" && !features.some((other) => sameName(other, trimmed))) {
      change([...features, trimmed]);
    }

    setTyped("");
  };

  const move = (from: number, to: number) => {
    const next = [...features];
    const [moved] = next.splice(from, 1);

    if (moved !== undefined) {
      next.splice(to, 0, moved);
      change(next);
    }
  };

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      data-field="features"
      className="flex flex-col gap-2 sm:col-span-2"
    >
      <span id={labelId} className="type-label text-ink">
        <Trans>Roles and features</Trans>
      </span>
      {features.length > 0 ? (
        <ol className="flex flex-col gap-1">
          {features.map((name, index) => (
            <PickedRow
              key={name}
              id={name}
              index={index}
              name={name}
              note={labelOf(name)}
              extra={undefined}
              isLast={index === features.length - 1}
              isReadOnly={locked}
              errors={fieldFindings(findings, `features[${String(index)}]`).problems}
              onMove={(to) => {
                move(index, to);
              }}
              onRemove={() => {
                change(features.filter((_, at) => at !== index));
              }}
            />
          ))}
        </ol>
      ) : null}
      <FieldErrorText errors={fieldFindings(findings, "features").problems} />
      {locked ? null : (
        <div className="flex items-start gap-2">
          <ComboBox
            label={<span className="sr-only">{t`Add a feature`}</span>}
            placeholder={t`Add a feature`}
            className="flex-1"
            mono
            allowsCustomValue
            inputValue={typed}
            value={null}
            onInputChange={setTyped}
            onChange={(key) => {
              if (key !== null) {
                add(String(key));
              }
            }}
          >
            {offered.map((feature) => (
              <ListBoxItem
                key={feature.name}
                id={feature.name}
                textValue={feature.name}
                description={i18n._(feature.label)}
              >
                {feature.name}
              </ListBoxItem>
            ))}
          </ComboBox>
          <Button
            isDisabled={typed.trim() === ""}
            onPress={() => {
              add(typed);
            }}
          >
            <Trans>Add</Trans>
          </Button>
        </div>
      )}
    </div>
  );
}
