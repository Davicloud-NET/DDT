// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import {
  Autocomplete,
  Header,
  Input,
  ListBox,
  ListBoxItem,
  ListBoxSection,
  SearchField,
  useFilter,
} from "react-aria-components";

import { cx } from "@/ui/cx";
import { fieldClass } from "@/ui/TextField";

import { valueKindLabel, type Subject, type SubjectSection } from "../conditionSubjects";
import { subjectSections } from "./subjectSections";

const itemClass =
  "flex cursor-pointer items-center justify-between gap-3 rounded-key px-2 py-1.5 type-body text-ink motion-highlight outline-none " +
  "focused:bg-hover selected:bg-selected selected:font-semibold";

// The subject picker's list, in sections, with a field that finds a subject by its name.
export function SubjectList({
  label,
  subject,
  subjects,
}: {
  label: string;
  subject: Subject;
  subjects: readonly Subject[];
}) {
  const { t } = useLingui();
  const { contains } = useFilter({ sensitivity: "base" });
  const titles: Record<SubjectSection, string> = {
    machine: t`Machine`,
    run: t`This run`,
    values: t`From rules and machine roles`,
    sequence: t`Variables and inputs of this sequence`,
  };
  const sections = subjectSections(subject, subjects);

  return (
    <Autocomplete filter={contains}>
      <SearchField aria-label={t`Find a fact or a variable`} autoFocus className="mb-1.5">
        <Input
          placeholder={t`Find a fact or a variable`}
          className={cx(fieldClass, "h-8 type-small")}
        />
      </SearchField>
      <ListBox
        aria-label={label}
        className="max-h-80 overflow-auto outline-none"
        renderEmptyState={() => (
          <p className="px-2 py-1.5 type-small text-muted">
            <Trans>Nothing has that name.</Trans>
          </p>
        )}
      >
        {sections.map(({ section, items }) => (
          <ListBoxSection key={section} id={section}>
            <Header className="px-2 pt-2 pb-1 type-small text-muted">{titles[section]}</Header>
            {items.map((item) => (
              <ListBoxItem
                key={item.name}
                id={item.name}
                textValue={
                  item.section === "machine" || item.section === "run"
                    ? `${item.label} ${item.name}`
                    : item.name
                }
                className={itemClass}
              >
                <span
                  className={cx(
                    "truncate",
                    item.section === "values" || item.section === "sequence"
                      ? "type-data"
                      : undefined,
                  )}
                >
                  {item.label}
                </span>
                <span className="shrink-0 type-small font-normal text-muted">
                  {item.note ?? valueKindLabel(item.kind)}
                </span>
              </ListBoxItem>
            ))}
          </ListBoxSection>
        ))}
      </ListBox>
    </Autocomplete>
  );
}
