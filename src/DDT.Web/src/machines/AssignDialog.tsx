// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";
import { Form } from "react-aria-components";

import {
  assignSequence,
  deploymentOptionsQuery,
  type AssignSequenceRequest,
  type DeploymentOptionsView,
} from "@/deployments/deployments";
import { ApiError } from "@/lib/api";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import {
  machineLabel,
  upsertMachine,
  WAITING_WINDOW_MS,
  type MachineSummary,
} from "@/machines/machines";
import { secureBootRisk } from "@/machines/secureBoot";
import { isRuleChoice, resolutionText, sequenceResolutionQuery } from "@/rules/rules";
import { canRun, sequencesQuery, type SequenceSummary } from "@/sequences/sequences";
import { Button } from "@/ui/Button";
import { Checkbox } from "@/ui/Checkbox";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

// Assigns a task sequence and says what that does to this machine before anything is sent: whether its disk is
// erased, and for a waiting machine whether the assignment also authorizes it. The answer is the machine as the
// server stored it, which replaces the one in the list; nothing is read again.
export function AssignDialog({
  machine,
  onClose,
}: {
  machine: MachineSummary;
  onClose: () => void;
}) {
  const { t: translate } = useLingui();
  const queryClient = useQueryClient();
  const sequences = useQuery(sequencesQuery);
  const resolution = useQuery(sequenceResolutionQuery(machine.id));
  const options = useQuery(deploymentOptionsQuery);
  // The server decides with its clock whether the machine waits at the prompt, so the dialog does too.
  const now = useNow(5_000) + (options.data?.serverClockOffsetMs ?? 0);

  const [chosenId, setChosenId] = useState<string | null>(null);
  const [computerName, setComputerName] = useState(machine.assignedName ?? "");
  const [nameProblem, setNameProblem] = useState<string | null>(null);
  // The sequence and image the allowance was given for, so another sequence, or another image written by the same
  // one after a live update, asks again.
  const [allowedFor, setAllowedFor] = useState<string | null>(null);
  const warningId = useId();

  const assign = useMutation({
    mutationFn: (request: AssignSequenceRequest) => assignSequence(machine.id, request),
    onSuccess: (updated) => {
      upsertMachine(queryClient, updated);
      onClose();
    },
  });

  const label = machineLabel(machine);
  const list = sequences.data ?? [];
  const runnable = list.filter(canRun);
  const ruleChoice =
    resolution.data !== undefined && isRuleChoice(resolution.data) ? resolution.data : null;
  // The rule's choice comes first, until the operator chooses.
  const sequence =
    runnable.find((candidate) => candidate.id === chosenId) ??
    runnable.find((candidate) => candidate.id === ruleChoice?.sequenceId) ??
    runnable[0] ??
    null;
  const erases = sequence?.erasesDisk === true;
  const sequenceName = sequence?.name ?? "";
  const risk = secureBootRisk(machine, sequence);
  const allowanceKey = sequence === null ? null : `${sequence.id} ${sequence.rawImageName ?? ""}`;
  const allowed = risk !== null && allowedFor !== null && allowedFor === allowanceKey;
  const nameRequired = sequence?.needsComputerName === true && machine.assignedName === null;
  const severalDisks = machine.eligibleDiskCount !== null && machine.eligibleDiskCount > 1;
  const serverNameProblem =
    assign.error instanceof ApiError
      ? (assign.error.problem?.errors?.computerName?.[0] ?? null)
      : null;
  const fieldProblem = nameProblem ?? serverNameProblem;
  const error = assign.isError && serverNameProblem === null ? assign.error.message : null;
  // Without the settings the dialog cannot say what the assignment does, so it does not offer it.
  const canSubmit =
    sequence !== null &&
    !(erases && severalDisks) &&
    !(risk?.required === true && !allowed) &&
    !assign.isPending &&
    options.data !== undefined &&
    !sequences.isPending;

  function submit() {
    if (sequence === null) {
      return;
    }

    const name = computerName.trim();

    if (nameRequired && name === "") {
      setNameProblem(nameRequiredText(sequence));
      return;
    }

    setNameProblem(null);
    assign.mutate({
      sequenceId: sequence.id,
      computerName: name === "" ? null : name,
      ...(allowed ? { allowSecureBootMismatch: true } : {}),
    });
  }

  const formId = `assign-${machine.id}`;

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Assign a task sequence to {label}</Trans>}
      isBusy={assign.isPending}
      width="lg"
      footer={
        <>
          <Button variant="secondary" isDisabled={assign.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button type="submit" form={formId} variant="primary" isDisabled={!canSubmit}>
            <Trans>Assign sequence</Trans>
          </Button>
        </>
      }
    >
      <Form
        id={formId}
        className="flex flex-col gap-4"
        validationBehavior="aria"
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <Select
          label={<Trans>Task sequence</Trans>}
          value={sequence?.id ?? null}
          isDisabled={runnable.length === 0}
          onChange={(key) => {
            setChosenId(key === null ? null : String(key));
            setAllowedFor(null);
          }}
          {...(ruleChoice === null ? {} : { hint: resolutionText(ruleChoice) })}
        >
          {list.map((candidate) => (
            <ListBoxItem
              key={candidate.id}
              id={candidate.id}
              isDisabled={!canRun(candidate)}
              {...(canRun(candidate) ? {} : { description: problemsText(candidate) })}
            >
              {candidate.name}
            </ListBoxItem>
          ))}
        </Select>

        {sequences.isError ? (
          <Notice tone="fail">
            <Trans>The task sequence list could not be loaded.</Trans>
          </Notice>
        ) : null}
        {options.isError ? (
          <Notice tone="fail">
            <Trans>
              The deployment settings could not be loaded, so DDT cannot say what the assignment
              does. Close this and try again.
            </Trans>
          </Notice>
        ) : null}
        {sequences.isSuccess && list.length === 0 ? (
          <p>
            <Trans>
              No task sequence exists yet. An administrator creates one under Deployment, Task
              sequences.
            </Trans>
          </p>
        ) : null}
        {list.length > 0 && runnable.length === 0 ? (
          <p>
            <Trans>
              Every task sequence has problems, so none can run. An administrator fixes them under
              Deployment, Task sequences.
            </Trans>
          </p>
        ) : null}

        <TextField
          label={<Trans>Computer name</Trans>}
          value={computerName}
          onChange={setComputerName}
          maxLength={15}
          autoComplete="off"
          spellCheck="false"
          mono
          isRequired={nameRequired}
          isInvalid={fieldProblem !== null}
          errorMessage={fieldProblem}
          hint={`${nameHint(machine, sequence)} ${translate`Up to 15 letters A to Z, digits and hyphens.`}`}
        />

        <div className="flex flex-col gap-2">
          {sequence !== null ? (
            erases ? (
              <Notice tone="attention">
                <Trans>
                  {sequenceName} erases all data on the disk of {label}.
                </Trans>
              </Notice>
            ) : (
              <p>
                <Trans>
                  {sequenceName} does not erase the disk of {label}.
                </Trans>
              </p>
            )
          ) : null}
          {risk !== null ? (
            <Notice tone="attention">
              <span id={warningId}>{risk.warning}</span>
            </Notice>
          ) : null}
          {sequence?.continuesInWindows === true ? (
            <p>
              <Trans>
                After the image is applied, the run goes on in the installed Windows, where the
                agent runs as a service until the run ends.
              </Trans>
            </p>
          ) : null}
          {sequence !== null && sequence.warningCount > 0 ? <p>{warningsText(sequence)}</p> : null}
          <p className="type-small text-muted">{hardwareText(machine)}</p>
          {erases && severalDisks ? (
            <Notice tone="fail">
              <Trans>
                This machine has more than one disk. Sign in at it and choose the disk there.
              </Trans>
            </Notice>
          ) : null}
          {erases && machine.eligibleDiskCount === 0 ? (
            <Notice tone="fail">
              <Trans>The machine reported no disk DDT can install on, so the run will fail.</Trans>
            </Notice>
          ) : null}
          {machine.state === "Pending" ? (
            <>
              <p>{lastSeenText(machine, now)}</p>
              {options.data !== undefined ? (
                <p>{pendingConsequence(machine, options.data, now)}</p>
              ) : null}
            </>
          ) : null}
        </div>

        {risk !== null ? (
          <Checkbox
            aria-describedby={warningId}
            isSelected={allowed}
            onChange={(selected) => {
              setAllowedFor(selected ? allowanceKey : null);
            }}
          >
            {risk.allowLabel}
          </Checkbox>
        ) : null}

        {error !== null ? <Notice tone="fail">{error}</Notice> : null}
      </Form>
    </Dialog>
  );
}

function problemsText(sequence: SequenceSummary): string {
  const count = sequence.problemCount;
  const problems = plural(count, { one: "# problem", other: "# problems" });

  return t`${problems}, cannot run`;
}

function warningsText(sequence: SequenceSummary): string {
  const name = sequence.name;
  const count = sequence.warningCount;
  const warnings = plural(count, { one: "# warning", other: "# warnings" });

  return t`${name} has ${warnings}. It runs, but look at the sequence first.`;
}

function hardwareText(machine: MachineSummary): string {
  const model = machine.model ?? t`not reported`;
  const disks = machine.disks;

  if (disks !== null) {
    return t`Model: ${model}. Reported disks: ${disks}.`;
  }

  // A machine that reported no eligible disk gets its own message.
  return machine.eligibleDiskCount === 0
    ? t`Model: ${model}.`
    : t`Model: ${model}. The machine has not reported its disks.`;
}

function lastSeenText(machine: MachineSummary, now: number): string {
  const when = relativeTime(machine.lastSeenUtc, now);
  const from = machine.lastSeenAddress;
  const by = machine.signedInBy;

  if (by === null) {
    return from === null
      ? t`Last seen ${when}; nobody has signed in at it.`
      : t`Last seen from ${from} ${when}; nobody has signed in at it.`;
  }

  return from === null
    ? t`Last seen ${when}; signed in by ${by}.`
    : t`Last seen from ${from} ${when}; signed in by ${by}.`;
}

function nameRequiredText(sequence: SequenceSummary): string {
  const name = sequence.name;

  return sequence.rawImageName === null
    ? t`Enter a computer name. ${name} joins the domain under this name.`
    : t`Enter a computer name. ${name} gives this name to the machine in its cloud-init seed.`;
}

// The server asks for a name only when the sequence uses it and the machine has none yet.
function nameHint(machine: MachineSummary, sequence: SequenceSummary | null): string {
  const uses = sequence?.needsComputerName === true;
  const current = machine.assignedName;

  if (current === null) {
    if (uses) {
      const name = sequence.name;

      return sequence.rawImageName === null
        ? t`Required, because ${name} joins the domain under this name.`
        : t`Required, because ${name} gives this name to the machine in its cloud-init seed.`;
    }

    return sequence?.rawImageName == null
      ? t`Optional. Without a name, Windows picks one.`
      : t`Optional. Without a name, the image picks one.`;
  }

  if (!uses) {
    return t`Optional. Left empty, the machine keeps the name ${current}.`;
  }

  return sequence.rawImageName === null
    ? t`Optional. Left empty, the machine keeps the name ${current} and joins the domain under it.`
    : t`Optional. Left empty, the machine keeps the name ${current}, which its cloud-init seed gets.`;
}

// What the assignment does to a machine that is not authorized yet, with now on the server's clock. Without web
// approval, the assignment authorizes a machine waiting at the prompt; one not seen for a while is authorized by
// the next sign-in at it, or by its next netboot from a zero touch network. With web approval, zero touch is off,
// and a sign-in and an assignment together authorize the machine in either order.
function pendingConsequence(
  machine: MachineSummary,
  options: DeploymentOptionsView,
  now: number,
): string {
  const by = machine.signedInBy;

  if (options.requireWebApproval) {
    return by === null
      ? t`It stays waiting until someone signs in at it.`
      : t`This also authorizes the machine, because ${by} signed in at it. It then runs the sequence and receives the deployment passwords.`;
  }

  if (now - Date.parse(machine.lastSeenUtc) <= WAITING_WINDOW_MS) {
    return t`This also authorizes the machine, which then runs the sequence and receives the deployment passwords.`;
  }

  return options.zeroTouchEnabled
    ? t`It stays waiting until someone signs in at it or it netboots from a zero touch network.`
    : t`It stays waiting until someone signs in at it.`;
}
