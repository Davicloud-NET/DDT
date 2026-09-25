// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";

import { Dialog } from "@/components/Dialog";
import {
  assignSequence,
  deploymentOptionsQuery,
  type AssignSequenceRequest,
  type DeploymentOptionsView,
} from "@/deployments/deployments";
import { ApiError } from "@/lib/api";
import { plural } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import {
  machineLabel,
  machinesQuery,
  upsertMachine,
  WAITING_WINDOW_MS,
  type MachineSummary,
} from "@/machines/machines";
import { secureBootRisk } from "@/machines/secureBoot";
import { isRuleChoice, sequenceResolutionQuery } from "@/rules/rules";
import { canRun, sequencesQuery, type SequenceSummary } from "@/sequences/sequences";

import styles from "./AssignDialog.module.scss";

export interface AssignDialogProps {
  machine: MachineSummary;
  onClose: () => void;
}

// Assigns a task sequence and says what that does to this machine before anything is sent: whether its disk is
// erased, and for a waiting machine whether the assignment also authorizes it.
export function AssignDialog({ machine, onClose }: AssignDialogProps) {
  const queryClient = useQueryClient();
  const sequences = useQuery(sequencesQuery);
  const resolution = useQuery(sequenceResolutionQuery(machine.id));
  const options = useQuery(deploymentOptionsQuery);
  // The server decides with its clock whether the machine waits at the prompt, so the dialog does too.
  const now = useNow(5_000) + (options.data?.serverClockOffsetMs ?? 0);
  const sequenceFieldId = useId();
  const sequenceHintId = useId();
  const nameFieldId = useId();
  const nameHintId = useId();
  const nameErrorId = useId();
  const allowId = useId();

  const [chosenId, setChosenId] = useState<string | null>(null);
  const [computerName, setComputerName] = useState(machine.assignedName ?? "");
  const [nameProblem, setNameProblem] = useState<string | null>(null);
  // Given for the sequence shown, so choosing another asks again.
  const [allowMismatch, setAllowMismatch] = useState(false);

  const assign = useMutation({
    mutationFn: (request: AssignSequenceRequest) => assignSequence(machine.id, request),
    onSuccess: (updated) => {
      upsertMachine(queryClient, updated);
      onClose();
    },
    // The refusal usually means the machine changed meanwhile, so show what is stored now.
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: machinesQuery.queryKey });
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
  const risk = secureBootRisk(machine, sequence);
  const allowed = risk !== null && allowMismatch;
  const nameRequired = sequence?.needsComputerName === true && machine.assignedName === null;
  const severalDisks = machine.eligibleDiskCount !== null && machine.eligibleDiskCount > 1;
  // A machine that reported no eligible disk has no disks line; the error below says so.
  const disksLine =
    machine.disks !== null
      ? `Reported disks: ${machine.disks}.`
      : machine.eligibleDiskCount === 0
        ? null
        : "The machine has not reported its disks.";
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
      setNameProblem(`Enter a computer name. ${sequence.name} ${nameUse(sequence)}.`);
      return;
    }

    setNameProblem(null);
    assign.mutate({
      sequenceId: sequence.id,
      computerName: name === "" ? null : name,
      ...(allowed ? { allowSecureBootMismatch: true } : {}),
    });
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !assign.isPending) {
          onClose();
        }
      }}
      title={`Assign a task sequence to ${label}`}
      description="Choose the task sequence to run and the name the computer gets."
    >
      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <div className={styles.field}>
          <label htmlFor={sequenceFieldId}>Task sequence</label>
          <select
            id={sequenceFieldId}
            value={sequence?.id ?? ""}
            disabled={runnable.length === 0}
            aria-describedby={ruleChoice === null ? undefined : sequenceHintId}
            onChange={(event) => {
              setChosenId(event.target.value);
              setAllowMismatch(false);
            }}
          >
            {list.map((candidate) => (
              <option key={candidate.id} value={candidate.id} disabled={!canRun(candidate)}>
                {optionLabel(candidate)}
              </option>
            ))}
          </select>
          {ruleChoice !== null && (
            <span id={sequenceHintId} className={styles.hint}>
              {ruleChoice.explanation}
            </span>
          )}
        </div>

        {sequences.isError && (
          <p className={styles.error}>The task sequence list could not be loaded.</p>
        )}
        {options.isError && (
          <p className={styles.error}>
            The deployment settings could not be loaded, so the dialog cannot say what the
            assignment does. Close it and try again.
          </p>
        )}
        {sequences.isSuccess && list.length === 0 && (
          <p className={styles.hint}>
            No task sequence exists yet. An administrator creates one on the Sequences page.
          </p>
        )}
        {list.length > 0 && runnable.length === 0 && (
          <p className={styles.hint}>
            Every task sequence has problems, so none can run. An administrator fixes them on the
            Sequences page.
          </p>
        )}

        <div className={styles.field}>
          <label htmlFor={nameFieldId}>Computer name</label>
          <input
            id={nameFieldId}
            value={computerName}
            maxLength={15}
            autoComplete="off"
            spellCheck={false}
            required={nameRequired}
            aria-invalid={fieldProblem !== null}
            aria-describedby={fieldProblem === null ? nameHintId : `${nameErrorId} ${nameHintId}`}
            onChange={(event) => {
              setComputerName(event.target.value);
            }}
          />
          {fieldProblem !== null && (
            <span id={nameErrorId} className={styles.error} role="alert">
              {fieldProblem}
            </span>
          )}
          <span id={nameHintId} className={styles.hint}>
            {nameHint(machine, sequence)} Up to 15 letters A to Z, digits and hyphens.
          </span>
        </div>

        <div className={styles.consequences}>
          {sequence !== null &&
            (erases ? (
              <p className={styles.warning}>
                {sequence.name} erases all data on the disk of {label}.
              </p>
            ) : (
              <p>
                {sequence.name} does not erase the disk of {label}.
              </p>
            ))}
          {risk !== null && <p className={styles.warning}>{risk.warning}</p>}
          {sequence?.continuesInWindows === true && (
            <p>
              After the image is applied, the run continues in the installed Windows, where the
              agent runs as a service until the run ends.
            </p>
          )}
          {sequence !== null && sequence.warningCount > 0 && (
            <p>
              {sequence.name} has {plural(sequence.warningCount, "warning")}. It runs, but look at
              the sequence first.
            </p>
          )}
          <p>
            Model: {machine.model ?? "not reported"}.{disksLine !== null && ` ${disksLine}`}
          </p>
          {erases && severalDisks && (
            <p className={styles.error}>
              This machine has more than one disk. Sign in at it and choose the disk there.
            </p>
          )}
          {erases && machine.eligibleDiskCount === 0 && (
            <p className={styles.error}>
              The machine reported no disk DDT can install on, so the run will fail.
            </p>
          )}
          {machine.state === "Pending" && (
            <>
              <p>
                Last seen
                {machine.lastSeenAddress === null ? "" : ` from ${machine.lastSeenAddress}`}{" "}
                {relativeTime(machine.lastSeenUtc, now)};{" "}
                {machine.signedInBy === null
                  ? "nobody has signed in at it."
                  : `signed in by ${machine.signedInBy}.`}
              </p>
              {options.data !== undefined && (
                <p>{pendingConsequence(machine, options.data, now)}</p>
              )}
            </>
          )}
        </div>

        {risk !== null && (
          <div className={styles.check}>
            <input
              id={allowId}
              type="checkbox"
              checked={allowMismatch}
              onChange={(event) => {
                setAllowMismatch(event.target.checked);
              }}
            />
            <label htmlFor={allowId}>{risk.allowLabel}</label>
          </div>
        )}

        {error !== null && (
          <p className={styles.error} role="alert">
            {error}
          </p>
        )}

        <div className={styles.actions}>
          <button
            type="button"
            className={styles.secondary}
            disabled={assign.isPending}
            onClick={onClose}
          >
            Close
          </button>
          <button type="submit" className={styles.submit} disabled={!canSubmit}>
            Assign sequence
          </button>
        </div>
      </form>
    </Dialog>
  );
}

function optionLabel(sequence: SequenceSummary): string {
  return canRun(sequence)
    ? sequence.name
    : `${sequence.name} (${plural(sequence.problemCount, "problem")}, cannot run)`;
}

// What a sequence that needs a computer name does with it: a Windows sequence joins the domain under it, and one that
// writes a raw disk image puts it in the cloud-init seed.
function nameUse(sequence: SequenceSummary): string {
  return sequence.rawImageName === null
    ? "joins the domain under this name"
    : "gives this name to the machine in its cloud-init seed";
}

// The server asks for a name only when the sequence uses it and the machine has none yet.
function nameHint(machine: MachineSummary, sequence: SequenceSummary | null): string {
  const uses = sequence?.needsComputerName === true;

  if (machine.assignedName === null) {
    if (uses) {
      return `Required, because ${sequence.name} ${nameUse(sequence)}.`;
    }

    return sequence?.rawImageName == null
      ? "Optional. Without a name, Windows picks one."
      : "Optional. Without a name, the image picks one.";
  }

  if (!uses) {
    return `Optional. Left empty, the machine keeps the name ${machine.assignedName}.`;
  }

  return sequence.rawImageName === null
    ? `Optional. Left empty, the machine keeps the name ${machine.assignedName} and joins the domain under it.`
    : `Optional. Left empty, the machine keeps the name ${machine.assignedName}, which its cloud-init seed gets.`;
}

// What the assignment does to a machine that is not authorized yet, with now on the server's clock.
// Without web approval, the assignment authorizes a machine waiting at the prompt; one not seen for a
// while is authorized by the next sign-in at it, or by its next netboot from a zero touch network. With
// web approval, zero touch is off, and a sign-in and an assignment together authorize the machine in
// either order: the assignment authorizes a machine someone already signed in at, however long ago it
// was seen, and otherwise the next sign-in at it does.
function pendingConsequence(
  machine: MachineSummary,
  options: DeploymentOptionsView,
  now: number,
): string {
  if (options.requireWebApproval) {
    return machine.signedInBy === null
      ? "It stays waiting until someone signs in at it."
      : `This also authorizes the machine, because ${machine.signedInBy} signed in at it. It then runs the sequence and receives the deployment passwords.`;
  }

  if (now - Date.parse(machine.lastSeenUtc) <= WAITING_WINDOW_MS) {
    return "This also authorizes the machine, which then runs the sequence and receives the deployment passwords.";
  }

  return options.zeroTouchEnabled
    ? "It stays waiting until someone signs in at it or it netboots from a zero touch network."
    : "It stays waiting until someone signs in at it.";
}
