// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconChevronDown, IconPlus, IconX } from "@tabler/icons-react";
import { useContext, useId, useState, type ReactNode } from "react";
import {
  Autocomplete,
  Button as AriaButton,
  Header,
  Input,
  ListBox,
  ListBoxItem,
  ListBoxSection,
  Popover,
  SearchField,
  Select,
  SelectValue,
  useFilter,
} from "react-aria-components";

import { EditorLock } from "@/sequences/editorLock";
import { ChoiceSetting, TextSetting } from "@/sequences/fields";
import {
  conditionPath,
  type ConditionChange,
  type ConditionField,
} from "@/sequences/flow/conditionTree";
import { fieldFindings, withFieldAt, type Findings } from "@/sequences/problems";
import type { ConditionGroup, ConditionNode, TestCondition } from "@/sequences/sequences";
import { Checkbox } from "@/ui/Checkbox";
import { cx } from "@/ui/cx";
import { TextField, fieldClass } from "@/ui/TextField";

import {
  conditionSentence,
  gigabytesOf,
  groupKinds,
  groupLabel,
  listOf,
  MAX_CONDITION_DEPTH,
  megabytesOf,
  newTestOf,
  operatorsFor,
  operatorTakesValue,
  operatorText,
  subjectFor,
  valueKindLabel,
  valueProblem,
  withSubject,
  type ConditionUse,
  type Subject,
  type SubjectSection,
  type ValueKind,
} from "./conditions";

// A condition as rows a person reads from top to bottom: a group says whether all, at least one or none of its rows
// must hold, each row tests one subject, and a group can hold another, four deep. Under it the condition is said in
// one sentence. Every control sits in an element named by its place, such as when.parts[1].value, so a finding can
// take the focus there. It edits nothing itself: each change goes to onChange with the path of the part it changes.

const noFindings: Findings = { problems: [], warnings: [] };

const iconKey =
  "flex size-8 shrink-0 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none " +
  "hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus";

const addKey =
  "flex cursor-pointer items-center gap-1.5 rounded-key px-1 py-1 type-label text-ink key-motion outline-none " +
  "hover:bg-hover pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus " +
  "disabled:cursor-not-allowed disabled:opacity-45";

export interface ConditionBuilderProps {
  label: ReactNode;
  hint?: ReactNode;
  // Where the condition is, for the sentence under it.
  use: ConditionUse;
  value: ConditionNode | null;
  subjects: readonly Subject[];
  // The member the condition is, for the places its controls are named by, such as "when".
  field?: ConditionField;
  // The place of a part as a finding names it, where it is not field and its parts.
  fieldOf?: (path: readonly number[]) => string;
  findings?: Findings;
  onChange: (path: readonly number[], change: ConditionChange) => void;
  isReadOnly?: boolean;
}

export function ConditionBuilder({
  label,
  hint,
  use,
  value,
  subjects,
  field = "when",
  fieldOf,
  findings = noFindings,
  onChange,
  isReadOnly = false,
}: ConditionBuilderProps) {
  const locked = useContext(EditorLock) || isReadOnly;
  const labelId = useId();
  const place = fieldOf ?? ((path: readonly number[]) => conditionPath(field, path));
  // A when or a rule may hold nothing and then holds always; an IF's test and a repeat's until keep an empty group.
  const mayBeNone = use === "when" || use === "rule";
  const numbers = numbering(value);
  const root = place([]);
  const own = fieldFindings(findings, root);

  const remove = (path: readonly number[]) => {
    const [only] = path;

    if (
      mayBeNone &&
      value !== null &&
      (path.length === 0 ||
        (path.length === 1 && only === 0 && value.kind !== "test" && value.parts.length === 1))
    ) {
      onChange([], { op: "set", node: null });
    } else {
      onChange(path, { op: "remove" });
    }
  };

  const context: RowContext = { subjects, place, findings, onChange, remove, locked, numbers };

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      data-field={root}
      className="@container flex flex-col gap-1.5"
    >
      <span id={labelId} className="type-label text-ink">
        {label}
      </span>
      {hint ? <span className="type-small text-muted">{hint}</span> : null}
      <div className="mt-1 flex flex-col gap-2.5 rounded-key bg-well p-3 shadow-[inset_0_0_0_1px_var(--color-line-soft)]">
        {value === null ? null : value.kind === "test" ? (
          <GroupRows
            group={{ kind: "all", parts: [value] }}
            path={[]}
            virtual
            depth={1}
            context={context}
          />
        ) : (
          <GroupRows group={value} path={[]} depth={1} context={context} />
        )}
        {value === null && locked ? (
          <p className="type-small text-muted">
            <Trans>No conditions.</Trans>
          </p>
        ) : null}
        {value === null && !locked ? <AddKeys path={[]} depth={1} context={context} /> : null}
      </div>
      <p className="type-small text-muted">{conditionSentence(use, value, subjects)}</p>
      {[...own.problems, ...own.warnings].length > 0 ? (
        <ul className="flex flex-col gap-0.5 type-small">
          {own.problems.map((message) => (
            <li key={message} className="text-fail-text">
              {message}
            </li>
          ))}
          {own.warnings.map((message) => (
            <li key={message} className="text-attention-text">
              {message}
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

interface RowContext {
  subjects: readonly Subject[];
  place: (path: readonly number[]) => string;
  findings: Findings;
  onChange: (path: readonly number[], change: ConditionChange) => void;
  remove: (path: readonly number[]) => void;
  locked: boolean;
  // Each test's number from 1 in reading order, by its path.
  numbers: ReadonlyMap<string, number>;
}

function numbering(value: ConditionNode | null): Map<string, number> {
  const numbers = new Map<string, number>();

  const visit = (node: ConditionNode, path: number[]) => {
    if (node.kind === "test") {
      numbers.set(path.join("."), numbers.size + 1);
    } else {
      node.parts.forEach((part, index) => {
        visit(part, [...path, index]);
      });
    }
  };

  if (value !== null) {
    visit(value, []);
  }

  return numbers;
}

// A group's choice of all, at least one or none, its rows, and the keys that add to it. virtual is a single test
// shown as a group of one: choosing another kind puts it into a group of that kind.
function GroupRows({
  group,
  path,
  depth,
  virtual = false,
  context,
}: {
  group: ConditionGroup;
  path: readonly number[];
  depth: number;
  virtual?: boolean;
  context: RowContext;
}) {
  const { t } = useLingui();
  const { place, findings, onChange, locked } = context;
  const where = place(path);

  return (
    <>
      <ChoiceSetting
        label={<span className="sr-only">{t`How the conditions combine`}</span>}
        field={virtual || path.length === 0 ? `${where}.kind` : where}
        findings={findings}
        className="w-60 max-w-full"
        value={group.kind}
        choices={groupKinds.map((kind) => ({ id: kind, label: groupLabel(kind) }))}
        onChange={(kind) => {
          if (kind === group.kind) {
            return;
          }

          const chosen = kind as ConditionGroup["kind"];

          onChange(path, virtual ? { op: "wrap", kind: chosen } : { op: "group", kind: chosen });
        }}
      />
      {group.parts.map((part, index) => {
        const at = virtual ? path : [...path, index];

        return part.kind === "test" ? (
          <TestRow key={index} test={part} path={at} context={context} />
        ) : (
          <div
            key={index}
            className="flex flex-col gap-2.5 rounded-key bg-panel p-3 shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
          >
            <GroupRows group={part} path={at} depth={depth + 1} context={context} />
            {locked ? null : (
              <div className="flex justify-end">
                <AriaButton
                  className={cx(addKey, "text-fail-text")}
                  onPress={() => {
                    context.remove(at);
                  }}
                >
                  <IconX aria-hidden="true" size={14} stroke={2} />
                  <Trans>Remove this group</Trans>
                </AriaButton>
              </div>
            )}
          </div>
        );
      })}
      {locked ? null : <AddKeys path={path} depth={depth} context={context} />}
    </>
  );
}

function AddKeys({
  path,
  depth,
  context,
}: {
  path: readonly number[];
  depth: number;
  context: RowContext;
}) {
  const { subjects, onChange } = context;
  const first = subjectFor(subjects, "Model");

  return (
    <div className="flex flex-wrap gap-x-4 gap-y-1">
      <AriaButton
        className={addKey}
        onPress={() => {
          onChange(path, { op: "add", part: newTestOf(first) });
        }}
      >
        <IconPlus aria-hidden="true" size={14} stroke={2} />
        <Trans>Add a condition</Trans>
      </AriaButton>
      <AriaButton
        className={addKey}
        isDisabled={depth >= MAX_CONDITION_DEPTH}
        onPress={() => {
          onChange(path, { op: "add", part: { kind: "any", parts: [newTestOf(first)] } });
        }}
      >
        <IconPlus aria-hidden="true" size={14} stroke={2} />
        <Trans>Add a group</Trans>
      </AriaButton>
    </div>
  );
}

function placeholderOf(kind: ValueKind, list: boolean): string | undefined {
  const example = {
    text: "Latitude",
    number: "4",
    memory: "16",
    yesNo: undefined,
    ipv4: "10.0.4.51",
    network: "10.20.4.0/24",
    mac: "00:15:5D:01:02:03",
    oneOf: undefined,
  }[kind];

  return example === undefined ? undefined : list ? `${example}; …` : example;
}

function TestRow({
  test,
  path,
  context,
}: {
  test: TestCondition;
  path: readonly number[];
  context: RowContext;
}) {
  const { t } = useLingui();
  const { subjects, place, findings, onChange, remove, locked, numbers } = context;
  const number = numbers.get(path.join(".")) ?? 1;
  const where = place(path);
  const subject = subjectFor(subjects, test.variable);
  const operators = operatorsFor(subject.kind);
  // A finding of the whole test shows at its value.
  const shown = withFieldAt(findings, where, `${where}.value`);
  const early = valueProblem(subject.kind, test.operator, test.value);

  const update = (patch: Partial<Omit<TestCondition, "kind">>) => {
    onChange(path, { op: "update", patch });
  };

  return (
    <div className="flex flex-col gap-1">
      {/* Narrow, as in the inspector, the value takes a line of its own under what is tested and how. */}
      <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_1.75rem] items-start gap-1.5 @lg:grid-cols-[minmax(0,10rem)_minmax(0,9rem)_minmax(0,1fr)_1.75rem]">
        <SubjectPicker
          label={t`What condition ${number} tests`}
          field={`${where}.variable`}
          findings={findings}
          subject={subject}
          subjects={subjects}
          locked={locked}
          onChange={(name) => {
            const { variable, operator, value } = withSubject(
              test,
              subject,
              subjectFor(subjects, name),
            );

            update({ variable, operator, value });
          }}
        />
        <ChoiceSetting
          label={<span className="sr-only">{t`Comparison of condition ${number}`}</span>}
          field={`${where}.operator`}
          findings={findings}
          value={test.operator}
          choices={[
            ...(operators.includes(test.operator) ? [] : [test.operator]),
            ...operators,
          ].map((operator) => ({ id: operator, label: operatorText(operator, subject.kind) }))}
          onChange={(operator) => {
            update({ operator: operator as TestCondition["operator"] });
          }}
        />
        {operatorTakesValue(test.operator) ? (
          <div className="col-span-2 row-start-2 @lg:col-span-1 @lg:row-start-auto">
            <ValueEditor
              label={t`Value of condition ${number}`}
              field={`${where}.value`}
              findings={shown}
              subject={subject}
              test={test}
              onChange={(value) => {
                update({ value });
              }}
            />
          </div>
        ) : null}
        {locked ? null : (
          <AriaButton
            aria-label={t`Remove condition ${number}`}
            className={cx(
              iconKey,
              "col-start-3 row-start-1 mt-3 size-7 @lg:col-start-auto @lg:row-start-auto",
            )}
            onPress={() => {
              remove(path);
            }}
          >
            <IconX size={14} stroke={2} />
          </AriaButton>
        )}
      </div>
      {early !== null && !locked ? <p className="type-small text-attention-text">{early}</p> : null}
    </div>
  );
}

function ValueEditor({
  label,
  field,
  findings,
  subject,
  test,
  onChange,
}: {
  label: string;
  field: string;
  findings: Findings;
  subject: Subject;
  test: TestCondition;
  onChange: (value: string) => void;
}) {
  const { t } = useLingui();
  const hidden = <span className="sr-only">{label}</span>;
  const list = test.operator === "In";

  if (subject.kind === "yesNo") {
    const lower = test.value.trim().toLowerCase();

    return (
      <ChoiceSetting
        label={hidden}
        field={field}
        findings={findings}
        value={["false", "no", "0"].includes(lower) ? "false" : "true"}
        choices={[
          { id: "true", label: t`yes` },
          { id: "false", label: t`no` },
        ]}
        onChange={onChange}
      />
    );
  }

  if (subject.kind === "oneOf" && list) {
    return (
      <ChoiceList
        label={label}
        field={field}
        subject={subject}
        value={test.value}
        onChange={onChange}
      />
    );
  }

  // Part of a value or a pattern is typed, as for text.
  if (subject.kind === "oneOf" && (test.operator === "Equals" || test.operator === "NotEquals")) {
    const known = subject.choices.some((choice) => choice.value === test.value);

    return (
      <ChoiceSetting
        label={hidden}
        field={field}
        findings={findings}
        value={test.value === "" ? null : test.value}
        placeholder={t`Choose`}
        choices={[
          ...(known || test.value === "" ? [] : [{ id: test.value, label: test.value }]),
          ...subject.choices.map((choice) => ({ id: choice.value, label: choice.label })),
        ]}
        onChange={onChange}
      />
    );
  }

  if (subject.kind === "memory") {
    return (
      <MemoryValue
        label={label}
        field={field}
        findings={findings}
        value={test.value}
        onChange={onChange}
      />
    );
  }

  const placeholder = placeholderOf(test.operator === "InSubnet" ? "network" : subject.kind, list);

  return (
    <TextSetting
      label={hidden}
      field={field}
      findings={findings}
      mono={subject.kind !== "text" && subject.kind !== "oneOf"}
      value={test.value}
      {...(placeholder === undefined ? {} : { placeholder })}
      onChange={onChange}
    />
  );
}

// Memory is entered in GB. What is typed stays while the field is typed in, so "1." is not turned into "1" before the
// next digit.
function MemoryValue({
  label,
  field,
  findings,
  value,
  onChange,
}: {
  label: string;
  field: string;
  findings: Findings;
  value: string;
  onChange: (value: string) => void;
}) {
  const locked = useContext(EditorLock);
  const [typed, setTyped] = useState<string | null>(null);
  const { problems } = fieldFindings(findings, field);

  return (
    <div data-field={field} className="flex items-center gap-1.5">
      <TextField
        label={<span className="sr-only">{label}</span>}
        value={typed ?? gigabytesOf(value)}
        isReadOnly={locked}
        mono
        inputMode="decimal"
        autoComplete="off"
        className="min-w-0 flex-1"
        isInvalid={problems.length > 0}
        errorMessage={problems.join(" ")}
        onChange={(text) => {
          setTyped(text);
          onChange(megabytesOf(text));
        }}
        onBlur={() => {
          setTyped(null);
        }}
      />
      <span aria-hidden="true" className="type-small text-muted">
        GB
      </span>
    </div>
  );
}

// Several of a list's choices, as a test with In takes them.
function ChoiceList({
  label,
  field,
  subject,
  value,
  onChange,
}: {
  label: string;
  field: string;
  subject: Subject;
  value: string;
  onChange: (value: string) => void;
}) {
  const locked = useContext(EditorLock);
  const chosen = listOf(value);

  return (
    <div
      data-field={field}
      role="group"
      aria-label={label}
      className="flex flex-wrap gap-x-3 gap-y-1 py-1.5"
    >
      {subject.choices.map((choice) => (
        <Checkbox
          key={choice.value}
          isSelected={chosen.includes(choice.value)}
          isReadOnly={locked}
          onChange={(selected) => {
            const next = selected
              ? [...chosen, choice.value]
              : chosen.filter((item) => item !== choice.value);

            onChange(
              subject.choices
                .map((item) => item.value)
                .filter((item) => next.includes(item))
                .join(";"),
            );
          }}
        >
          <span className="type-small">{choice.label}</span>
        </Checkbox>
      ))}
    </div>
  );
}

const itemClass =
  "flex cursor-pointer items-center justify-between gap-3 rounded-key px-2 py-1.5 type-body text-ink motion-highlight outline-none " +
  "focused:bg-hover selected:bg-selected selected:font-semibold";

// Chooses what a test tests, from the sections of subjects, with a field that finds one by its name.
function SubjectPicker({
  label,
  field,
  findings,
  subject,
  subjects,
  locked,
  onChange,
}: {
  label: string;
  field: string;
  findings: Findings;
  subject: Subject;
  subjects: readonly Subject[];
  locked: boolean;
  onChange: (name: string) => void;
}) {
  const { t } = useLingui();
  const { contains } = useFilter({ sensitivity: "base" });
  const { problems } = fieldFindings(findings, field);
  const listed = subjects.some((candidate) => candidate.name === subject.name);
  const all = listed ? subjects : [...subjects, subject];
  const titles: Record<SubjectSection, string> = {
    machine: t`Machine`,
    run: t`This run`,
    values: t`From rules and machine roles`,
    sequence: t`Variables and inputs of this sequence`,
  };
  const sections = (["machine", "run", "values", "sequence"] as const)
    .map((section) => ({ section, items: all.filter((item) => item.section === section) }))
    .filter((section) => section.items.length > 0);

  if (locked) {
    return (
      <div data-field={field}>
        <TextField
          label={<span className="sr-only">{label}</span>}
          value={subject.label}
          isReadOnly
        />
      </div>
    );
  }

  // Beside the fields with a label kept for screen readers, which stand as far down as its gap.
  return (
    <div data-field={field} className="pt-1.5">
      <Select
        aria-label={label}
        value={subject.name}
        isInvalid={problems.length > 0}
        onChange={(key) => {
          if (key !== null && String(key) !== subject.name) {
            onChange(String(key));
          }
        }}
      >
        <AriaButton
          className={cx(
            fieldClass,
            "flex h-9.5 cursor-pointer items-center gap-1.5 pr-1.5 text-left type-body",
            problems.length > 0 && "shadow-[inset_0_0_0_1.5px_var(--color-fail-text)]",
          )}
        >
          <SelectValue className="flex-1 truncate">{() => subject.label}</SelectValue>
          <IconChevronDown
            aria-hidden="true"
            size={14}
            stroke={2}
            className="shrink-0 text-muted"
          />
        </AriaButton>
        <Popover
          offset={4}
          placement="bottom start"
          className="flex w-72 max-w-[calc(100vw-2rem)] flex-col rounded-overlay bg-raised p-2 shadow-overlay outline-none entering:animate-pop-in exiting:animate-pop-out"
        >
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
                  <Header className="px-2 pt-2 pb-1 type-small text-muted">
                    {titles[section]}
                  </Header>
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
        </Popover>
      </Select>
      {problems.length > 0 ? (
        <span className="type-small text-fail-text">{problems.join(" ")}</span>
      ) : null}
    </div>
  );
}
