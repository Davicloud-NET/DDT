// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus, IconX } from "@tabler/icons-react";
import { useContext } from "react";
import { Button as AriaButton } from "react-aria-components";

import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";

import { EditorLock } from "../../editorLock";
import { TextSetting } from "../../fields/TextSetting";
import type { InputChoice } from "../../sequences";
import { iconKey, orNull, type InputPartProps } from "./declarationFields";

// The choices of an input that takes one or several of a list, each a value and the label a person reads.
export function InputChoices({ input, at, findings, update }: InputPartProps) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);

  return (
    <div data-field={at("choices")} className="flex flex-col gap-2">
      <span className="type-label text-ink">
        <Trans>Choices</Trans>
      </span>
      {input.choices.map((choice, position) => {
        const number = position + 1;
        const setChoice = (patch: Partial<InputChoice>) => {
          update({
            choices: input.choices.map((other, place) =>
              place === position ? { ...other, ...patch } : other,
            ),
          });
        };

        return (
          <div
            key={position}
            className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_2rem] items-start gap-1.5"
          >
            <TextSetting
              label={<span className="sr-only">{t`Value of choice ${number}`}</span>}
              field={at(`choices[${String(position)}].value`)}
              findings={findings}
              mono
              placeholder={t`Value`}
              value={choice.value}
              onChange={(value) => {
                setChoice({ value });
              }}
            />
            <TextSetting
              label={<span className="sr-only">{t`Label of choice ${number}`}</span>}
              field={at(`choices[${String(position)}].label`)}
              findings={findings}
              placeholder={t`Label`}
              value={choice.label ?? ""}
              onChange={(text) => {
                setChoice({ label: orNull(text) });
              }}
            />
            {locked ? null : (
              <AriaButton
                aria-label={t`Remove choice ${number}`}
                className={cx(iconKey, "mt-1")}
                onPress={() => {
                  update({ choices: input.choices.filter((_, other) => other !== position) }, true);
                }}
              >
                <IconX size={14} stroke={2} />
              </AriaButton>
            )}
          </div>
        );
      })}
      {locked ? null : (
        <div>
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              update({ choices: [...input.choices, { value: "", label: null }] }, true);
            }}
          >
            <IconPlus aria-hidden="true" size={16} stroke={2} />
            <Trans>Add a choice</Trans>
          </Button>
        </div>
      )}
    </div>
  );
}
