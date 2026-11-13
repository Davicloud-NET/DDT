// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useContext } from "react";

import { OrderedPicker } from "@/applications/drawer/OrderedPicker";
import { useLibraryEntries } from "@/applications/useLibraryEntries";
import { FieldErrorText } from "@/ui/FieldErrorText";
import { Checkbox } from "@/ui/Checkbox";

import { EditorLock } from "../../editorLock";
import { fieldFindings } from "../../problems";
import type { ApplicationReference } from "../../sequences";
import type { InputPartProps } from "./declarationFields";

// What an Applications input offers, in order, and which entries start ticked. One no longer in the library stays,
// marked, until it's removed.
export function InputApplications({ input, at, findings, update }: InputPartProps) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const entries = useLibraryEntries();
  const offered = input.offered ?? [];
  const ticked = input.ticked ?? [];
  const bundle = t`Bundle`;
  const options = entries.map((entry) =>
    entry.kind === "Bundle" ? { ...entry, note: bundle } : entry,
  );
  const referenceOf = (id: string): ApplicationReference => ({
    id,
    kind: (
      entries.find((entry) => entry.id === id) ??
      offered.find((reference) => reference.id === id) ?? { kind: "Application" as const }
    ).kind,
  });
  const tickedProblems = [
    ...fieldFindings(findings, at("ticked")).problems,
    ...ticked.flatMap(
      (_, index) => fieldFindings(findings, at(`ticked[${String(index)}]`)).problems,
    ),
  ];

  return (
    <div data-field={at("ticked")} className="flex flex-col gap-1">
      <OrderedPicker
        label={<Trans>Offered</Trans>}
        field={at("offered")}
        ids={offered.map((reference) => reference.id)}
        options={options}
        addLabel={t`Add an application or bundle`}
        findings={findings}
        isReadOnly={locked}
        onChange={(ids) => {
          update(
            { offered: ids.map(referenceOf), ticked: ticked.filter((id) => ids.includes(id)) },
            true,
          );
        }}
        rowExtra={(id, shown) => (
          <Checkbox
            aria-label={t`${shown} starts ticked`}
            isSelected={ticked.includes(id)}
            isDisabled={locked}
            onChange={(on) => {
              const next = offered
                .map((reference) => reference.id)
                .filter((other) => (other === id ? on : ticked.includes(other)));

              update({ ticked: next }, true);
            }}
            className="shrink-0 type-small text-muted"
          >
            <Trans>Ticked</Trans>
          </Checkbox>
        )}
      />
      <FieldErrorText errors={tickedProblems} />
    </div>
  );
}
