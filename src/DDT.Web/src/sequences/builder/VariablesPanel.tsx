// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import {
  IconArrowDown,
  IconArrowUp,
  IconChevronDown,
  IconChevronRight,
  IconPlus,
  IconTrash,
  IconX,
} from "@tabler/icons-react";
import { useContext, useState, type ReactNode } from "react";
import { Button as AriaButton } from "react-aria-components";

import { Button } from "@/ui/Button";
import { buttonClass } from "@/ui/buttonClass";
import { cx } from "@/ui/cx";
import { TextField } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import { ChoiceSetting, FlagSetting, TextSetting } from "../fields";
import { namePattern, type FlowEdit } from "../flow/flowEdits";
import { nodeTitle } from "../flow/flowKeyboard";
import { findNode } from "../flow/flowTree";
import { usedBy } from "../flow/references";
import type { Findings } from "../problems";
import type { SequenceDraft } from "../sequenceDraft";
import type { InputAsk, InputDeclaration, InputKind, VariableDeclaration } from "../sequences";
import { TemplateField } from "./TemplateField";

export interface OpenRow {
  list: "variables" | "inputs";
  index: number;
}

const iconKey =
  "flex size-7.5 shrink-0 cursor-pointer items-center justify-center rounded-key text-ink-2 key-motion outline-none " +
  "hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus " +
  "disabled:cursor-not-allowed disabled:opacity-40";

function orNull(text: string): string | null {
  return text.trim() === "" ? null : text;
}

function freeName(draft: SequenceDraft, stem: string): string {
  const taken = [...draft.variables, ...draft.inputs].map((item) => item.name.toLowerCase());
  let number = 1;

  while (taken.includes(`${stem}${String(number)}`.toLowerCase())) {
    number++;
  }

  return `${stem}${String(number)}`;
}

// The sequence's variables and inputs in one list, as the document holds them: a variable is a value with a default
// that rules, machine roles and steps may change, an input a question asked before the run starts, whose answer sets
// the variable of its name. Each says which nodes use it; a rename changes every one of them.
export function VariablesPanel({
  draft,
  findings,
  open,
  onEdit,
  onGoToNode,
}: {
  draft: SequenceDraft;
  // The findings of the sequence's own, such as variables[1].name.
  findings: Findings;
  // A row a finding points at, which opens.
  open: OpenRow | null;
  onEdit: (edit: FlowEdit) => void;
  onGoToNode: (id: string) => void;
}) {
  const locked = useContext(EditorLock);
  const [opened, setOpened] = useState<readonly string[]>(
    open === null ? [] : [`${open.list}:${String(open.index)}`],
  );
  // A row a finding points at opens, and can be closed again like any other.
  const [pointed, setPointed] = useState(open);

  if (open !== pointed) {
    setPointed(open);

    if (open !== null) {
      const key = `${open.list}:${String(open.index)}`;

      setOpened((keys) => (keys.includes(key) ? keys : [...keys, key]));
    }
  }

  const toggle = (key: string) => {
    setOpened((keys) =>
      keys.includes(key) ? keys.filter((other) => other !== key) : [...keys, key],
    );
  };
  const isOpen = (list: OpenRow["list"], index: number) =>
    opened.includes(`${list}:${String(index)}`);
  const empty = draft.variables.length === 0 && draft.inputs.length === 0;

  return (
    <div className="flex flex-col gap-4">
      <p className="type-small text-ink-2">
        <Trans>
          A variable is a value with a default, which rules, machine roles and steps may change. An
          input is asked on the web or at the machine before the run starts, and its answer sets the
          variable of its name.
        </Trans>
      </p>
      {empty ? (
        <p className="type-small text-muted">
          <Trans>This sequence declares no variables or inputs yet.</Trans>
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {draft.variables.map((variable, index) => (
            <Row
              key={`v:${variable.name}`}
              list="variables"
              index={index}
              count={draft.variables.length}
              name={variable.name}
              tag={<Trans>Variable</Trans>}
              draft={draft}
              isOpen={isOpen("variables", index)}
              onToggle={() => {
                toggle(`variables:${String(index)}`);
              }}
              onEdit={onEdit}
              onGoToNode={onGoToNode}
            >
              <VariableFields
                variable={variable}
                index={index}
                findings={findings}
                onEdit={onEdit}
              />
            </Row>
          ))}
          {draft.inputs.map((input, index) => (
            <Row
              key={`i:${input.name}`}
              list="inputs"
              index={index}
              count={draft.inputs.length}
              name={input.name}
              tag={askedTag(input.askAt)}
              draft={draft}
              isOpen={isOpen("inputs", index)}
              onToggle={() => {
                toggle(`inputs:${String(index)}`);
              }}
              onEdit={onEdit}
              onGoToNode={onGoToNode}
            >
              <InputFields input={input} index={index} findings={findings} onEdit={onEdit} />
            </Row>
          ))}
        </ul>
      )}
      {locked ? null : (
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            onPress={() => {
              const name = freeName(draft, "Variable");

              onEdit({
                type: "addVariable",
                variable: { name, default: null, description: null, setBySteps: false },
              });
              setOpened((keys) => [...keys, `variables:${String(draft.variables.length)}`]);
            }}
          >
            <IconPlus aria-hidden="true" size={16} stroke={2} />
            <Trans>Add a variable</Trans>
          </Button>
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              const name = freeName(draft, "Input");

              onEdit({
                type: "addInput",
                input: {
                  name,
                  label: name,
                  help: null,
                  kind: "Text",
                  choices: [],
                  default: null,
                  required: false,
                  maxLength: null,
                  askAt: "Web",
                  account: null,
                },
              });
              setOpened((keys) => [...keys, `inputs:${String(draft.inputs.length)}`]);
            }}
          >
            <IconPlus aria-hidden="true" size={16} stroke={2} />
            <Trans>Add an input</Trans>
          </Button>
        </div>
      )}
    </div>
  );
}

function askedTag(ask: InputAsk) {
  switch (ask) {
    case "Web":
      return <Trans>Input, asked on the web</Trans>;
    case "Machine":
      return <Trans>Input, asked at the machine</Trans>;
    case "Both":
      return <Trans>Input, asked on the web or at the machine</Trans>;
  }
}

function Row({
  list,
  index,
  count,
  name,
  tag,
  draft,
  isOpen,
  onToggle,
  onEdit,
  onGoToNode,
  children,
}: {
  list: OpenRow["list"];
  index: number;
  count: number;
  name: string;
  tag: ReactNode;
  draft: SequenceDraft;
  isOpen: boolean;
  onToggle: () => void;
  onEdit: (edit: FlowEdit) => void;
  onGoToNode: (id: string) => void;
  children: ReactNode;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const users = usedBy(draft, name);
  const uses = users.length;
  const Chevron = isOpen ? IconChevronDown : IconChevronRight;
  const move = (to: number) => {
    onEdit(
      list === "variables" ? { type: "moveVariable", name, to } : { type: "moveInput", name, to },
    );
  };

  return (
    <li className="rounded-key bg-raised shadow-[inset_0_0_0_1px_var(--color-line-soft)]">
      <div className="flex items-center gap-1 px-1.5 py-1.5">
        <AriaButton
          aria-expanded={isOpen}
          className="flex min-w-0 flex-1 cursor-pointer items-center gap-2 rounded-key px-1.5 py-1 text-left motion-colors outline-none hover:bg-hover focus-visible:outline-2 focus-visible:outline-focus"
          onPress={onToggle}
        >
          <Chevron aria-hidden="true" size={14} stroke={2} className="shrink-0 text-muted" />
          <span className="flex min-w-0 flex-col">
            <span className="truncate type-data text-ink">{name}</span>
            <span className="type-small text-muted">
              {tag}
              {" · "}
              {uses === 0
                ? t`Not used yet`
                : plural(uses, { one: "Used by # node", other: "Used by # nodes" })}
            </span>
          </span>
        </AriaButton>
        {locked ? null : (
          <>
            <AriaButton
              aria-label={t`Move ${name} up`}
              isDisabled={index === 0}
              className={iconKey}
              onPress={() => {
                move(index - 1);
              }}
            >
              <IconArrowUp size={14} stroke={2} />
            </AriaButton>
            <AriaButton
              aria-label={t`Move ${name} down`}
              isDisabled={index === count - 1}
              className={iconKey}
              onPress={() => {
                move(index + 1);
              }}
            >
              <IconArrowDown size={14} stroke={2} />
            </AriaButton>
          </>
        )}
      </div>
      {isOpen ? (
        <div className="flex flex-col gap-4 border-t border-line-soft px-3 pt-3 pb-4">
          <RenameField list={list} index={index} name={name} draft={draft} onEdit={onEdit} />
          {children}
          {uses > 0 ? (
            <div className="flex flex-col gap-1">
              <span className="type-label text-ink">
                <Trans>Used by</Trans>
              </span>
              <ul className="flex flex-col">
                {users.map((id) => {
                  const node = findNode(draft.steps, id);
                  const title = node === undefined ? id : nodeTitle(node);

                  return (
                    <li key={id}>
                      <AriaButton
                        aria-label={t`Go to ${title}`}
                        className="cursor-pointer rounded-key px-1 py-0.5 text-left type-small text-ink-2 underline outline-none hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
                        onPress={() => {
                          onGoToNode(id);
                        }}
                      >
                        {title}
                      </AriaButton>
                    </li>
                  );
                })}
              </ul>
            </div>
          ) : null}
          {locked ? null : (
            <div>
              <AriaButton
                aria-label={t`Remove ${name}`}
                className={buttonClass("quiet", "sm", "text-fail-text hover:text-fail-text")}
                onPress={() => {
                  onEdit(
                    list === "variables"
                      ? { type: "removeVariable", name }
                      : { type: "removeInput", name },
                  );
                }}
              >
                <IconTrash aria-hidden="true" size={16} stroke={2} />
                <Trans>Remove</Trans>
              </AriaButton>
            </div>
          )}
        </div>
      ) : null}
    </li>
  );
}

// A name is changed everywhere at once: in templates, conditions, Set variable steps and account references, and for
// the variable and the input of that name alike. The field holds what is typed until then.
function RenameField({
  list,
  index,
  name,
  draft,
  onEdit,
}: {
  list: OpenRow["list"];
  index: number;
  name: string;
  draft: SequenceDraft;
  onEdit: (edit: FlowEdit) => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const [typed, setTyped] = useState(name);
  const next = typed.trim();
  const valid = namePattern.test(next);
  const lower = next.toLowerCase();
  const taken =
    lower !== name.toLowerCase() &&
    [...draft.variables, ...draft.inputs].some((item) => item.name.toLowerCase() === lower);
  const field = `${list}[${String(index)}].name`;
  const placeholder = `{{${name}}}`;

  return (
    <div data-field={field} className="flex flex-col gap-2">
      <TextField
        label={<Trans>Name</Trans>}
        value={locked ? name : typed}
        isReadOnly={locked}
        mono
        spellCheck="false"
        autoComplete="off"
        onChange={setTyped}
        isInvalid={!valid || taken}
        errorMessage={
          !valid
            ? t`A name starts with a letter and holds only letters, digits and _, at most 64 in all.`
            : t`The sequence already has a variable or an input of that name.`
        }
        hint={t`Templates use it as ${placeholder}.`}
      />
      {locked || next === name ? null : (
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            isDisabled={!valid || taken}
            onPress={() => {
              onEdit({ type: "renameVariable", from: name, to: next });
            }}
          >
            <Trans>Rename everywhere</Trans>
          </Button>
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              setTyped(name);
            }}
          >
            <Trans>Keep {name}</Trans>
          </Button>
        </div>
      )}
    </div>
  );
}

function VariableFields({
  variable,
  index,
  findings,
  onEdit,
}: {
  variable: VariableDeclaration;
  index: number;
  findings: Findings;
  onEdit: (edit: FlowEdit) => void;
}) {
  const at = (member: string) => `variables[${String(index)}].${member}`;
  const name = variable.name;
  const update = (patch: Partial<Omit<VariableDeclaration, "name">>, chosen = false) => {
    onEdit({ type: "updateVariable", name, patch, ...(chosen ? { chosen } : {}) });
  };

  return (
    <>
      <TemplateField
        label={<Trans>Default</Trans>}
        field={at("default")}
        findings={findings}
        hint={
          <Trans>What the variable holds unless an input, a rule or a machine role sets it.</Trans>
        }
        value={variable.default ?? ""}
        onChange={(text) => {
          update({ default: orNull(text) });
        }}
      />
      <TextSetting
        label={<Trans>Description</Trans>}
        field={at("description")}
        findings={findings}
        value={variable.description ?? ""}
        onChange={(text) => {
          update({ description: orNull(text) });
        }}
      />
      <FlagSetting
        label={<Trans>Steps may change it</Trans>}
        field={at("setBySteps")}
        findings={findings}
        hint={
          <Trans>
            Set variable steps and scripts may give it a new value while the run goes on.
          </Trans>
        }
        value={variable.setBySteps}
        onChange={(setBySteps) => {
          update({ setBySteps }, true);
        }}
      />
    </>
  );
}

const inputKinds: InputKind[] = ["Text", "Choice", "MultiChoice", "YesNo", "Account"];

const askAts: InputAsk[] = ["Web", "Machine", "Both"];

function InputFields({
  input,
  index,
  findings,
  onEdit,
}: {
  input: InputDeclaration;
  index: number;
  findings: Findings;
  onEdit: (edit: FlowEdit) => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const at = (member: string) => `inputs[${String(index)}].${member}`;
  const name = input.name;
  const update = (patch: Partial<Omit<InputDeclaration, "name">>, chosen = false) => {
    onEdit({ type: "updateInput", name, patch, ...(chosen ? { chosen } : {}) });
  };
  const kindLabels: Record<InputKind, string> = {
    Text: t`Text`,
    Choice: t`One of a list`,
    MultiChoice: t`Several of a list`,
    YesNo: t`Yes or no`,
    Account: t`An account, for the run alone`,
  };
  const askLabels: Record<InputAsk, string> = {
    Web: t`On the web, when the run is assigned or approved`,
    Machine: t`At the machine`,
    Both: t`On the web or at the machine`,
  };
  const listed = input.kind === "Choice" || input.kind === "MultiChoice";
  const destination = input.account ?? { domain: null, hosts: [], runAs: false };

  return (
    <>
      <TextSetting
        label={<Trans>Question</Trans>}
        field={at("label")}
        findings={findings}
        hint={<Trans>What the person who answers reads.</Trans>}
        value={input.label}
        onChange={(label) => {
          update({ label });
        }}
      />
      <TextSetting
        label={<Trans>Help</Trans>}
        field={at("help")}
        findings={findings}
        value={input.help ?? ""}
        onChange={(text) => {
          update({ help: orNull(text) });
        }}
      />
      <ChoiceSetting
        label={<Trans>Answer</Trans>}
        field={at("kind")}
        findings={findings}
        value={input.kind}
        choices={inputKinds.map((kind) => ({ id: kind, label: kindLabels[kind] }))}
        onChange={(chosen) => {
          const kind = chosen as InputKind;

          update(
            {
              kind,
              account:
                kind === "Account"
                  ? (input.account ?? { domain: null, hosts: [], runAs: false })
                  : null,
              choices: kind === "Choice" || kind === "MultiChoice" ? input.choices : [],
            },
            true,
          );
        }}
      />
      {listed ? (
        <div data-field={at("choices")} className="flex flex-col gap-2">
          <span className="type-label text-ink">
            <Trans>Choices</Trans>
          </span>
          {input.choices.map((choice, position) => {
            const number = position + 1;
            const setChoice = (patch: Partial<typeof choice>) => {
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
                      update(
                        { choices: input.choices.filter((_, other) => other !== position) },
                        true,
                      );
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
      ) : null}
      {input.kind === "Account" ? (
        <>
          <TextSetting
            label={<Trans>Domain it joins</Trans>}
            field={at("account.domain")}
            findings={findings}
            hint={<Trans>For a Join the domain step. Empty where it joins none.</Trans>}
            mono
            value={destination.domain ?? ""}
            onChange={(text) => {
              update({ account: { ...destination, domain: orNull(text) } });
            }}
          />
          <TextSetting
            label={<Trans>Share hosts it may connect to</Trans>}
            field={at("account.hosts")}
            findings={findings}
            hint={<Trans>Host names separated by commas, such as fs01.corp.example.</Trans>}
            mono
            value={destination.hosts.join(", ")}
            onChange={(text) => {
              update({
                account: {
                  ...destination,
                  hosts: text
                    .split(",")
                    .map((host) => host.trim())
                    .filter((host) => host !== ""),
                },
              });
            }}
          />
          <FlagSetting
            label={<Trans>Scripts may run as it</Trans>}
            field={at("account.runAs")}
            findings={findings}
            value={destination.runAs}
            onChange={(runAs) => {
              update({ account: { ...destination, runAs } }, true);
            }}
          />
        </>
      ) : input.kind === "YesNo" ? (
        <ChoiceSetting
          label={<Trans>Default</Trans>}
          field={at("default")}
          findings={findings}
          value={input.default ?? "none"}
          choices={[
            { id: "none", label: t`No default` },
            { id: "true", label: t`Yes` },
            { id: "false", label: t`No` },
          ]}
          onChange={(value) => {
            update({ default: value === "none" ? null : value }, true);
          }}
        />
      ) : (
        <TextSetting
          label={<Trans>Default</Trans>}
          field={at("default")}
          findings={findings}
          hint={
            listed ? (
              <Trans>The value of a choice; several separated by semicolons.</Trans>
            ) : undefined
          }
          value={input.default ?? ""}
          onChange={(text) => {
            update({ default: orNull(text) });
          }}
        />
      )}
      {input.kind === "Text" ? (
        <TextSetting
          label={<Trans>At most this many characters</Trans>}
          field={at("maxLength")}
          findings={findings}
          hint={<Trans>Empty for no limit.</Trans>}
          mono
          value={input.maxLength === null ? "" : String(input.maxLength)}
          onChange={(text) => {
            const number = Number(text);

            if (text.trim() === "") {
              update({ maxLength: null });
            } else if (Number.isInteger(number) && number > 0 && number <= 1024) {
              update({ maxLength: number });
            }
          }}
        />
      ) : null}
      <ChoiceSetting
        label={<Trans>Asked</Trans>}
        field={at("askAt")}
        findings={findings}
        value={input.askAt}
        choices={askAts.map((ask) => ({ id: ask, label: askLabels[ask] }))}
        onChange={(ask) => {
          update({ askAt: ask as InputAsk }, true);
        }}
      />
      <FlagSetting
        label={<Trans>An answer is required</Trans>}
        field={at("required")}
        findings={findings}
        hint={<Trans>Without one, and without a default, the run waits at its start.</Trans>}
        value={input.required}
        onChange={(required) => {
          update({ required }, true);
        }}
      />
    </>
  );
}
