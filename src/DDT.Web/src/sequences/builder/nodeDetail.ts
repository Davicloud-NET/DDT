// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";

import type { AccountView } from "@/accounts/accounts";
import { conditionSummary, legacyTree } from "@/conditions/conditions";
import type { Subject } from "@/conditions/conditionSubjects";

import { findingText, type Findings } from "../problems";
import type {
  AccountReference,
  SequenceStep,
  WindowsUpdateStep,
  WriteUnattendStep,
} from "../sequences";
import { EMPTY_ID, inPhase, interpreterLabel } from "../steps";
import type { StepCatalog } from "../useStepCatalog";

// The line a node's card shows under its name. That's its first problem if it has one. Otherwise it's when the node
// runs and what it does, such as the image it applies or the value it sets. code marks a value such as a template.
export interface NodeDetail {
  text: string;
  code: boolean;
  problem: boolean;
}

function imageName(catalog: StepCatalog, id: string): string {
  if (id === EMPTY_ID) {
    return t`No image chosen yet`;
  }

  return catalog.images.find((image) => image.id === id)?.name ?? t`Image deleted`;
}

function accountName(accounts: readonly AccountView[] | null, reference: AccountReference): string {
  const input = reference.input;

  if (input !== null) {
    return t`With ${input}, asked for the run`;
  }

  const name = accounts?.find((account) => account.id === reference.accountId)?.name ?? null;

  return name === null ? t`With a stored account` : t`With ${name}`;
}

function applicationsDetail(count: number, variable: string | null): string {
  if (variable === null) {
    return count === 0
      ? t`No applications chosen yet`
      : plural(count, { one: "# application or bundle", other: "# applications and bundles" });
  }

  return count === 0
    ? t`The applications ${variable} names`
    : plural(count, {
        one: `# application or bundle, then those ${variable} names`,
        other: `# applications and bundles, then those ${variable} names`,
      });
}

// The answer file's own choices, or what it is for when it takes every default.
function unattendDetail(node: WriteUnattendStep): string {
  const parts = [node.uiLanguage, node.organization].filter(
    (part): part is string => part !== null && part !== "",
  );

  if (!(
    node.hideEulaPage &&
    node.hideOemRegistration &&
    node.hideOnlineAccounts &&
    node.hideWirelessSetup
  )) {
    parts.push(t`shows some setup pages`);
  }

  if (node.autoLogon !== "None") {
    parts.push(t`signs in automatically`);
  }

  const commands = node.firstLogonCommands.length;

  if (commands > 0) {
    parts.push(
      plural(commands, {
        one: "# command at the first sign-in",
        other: "# commands at the first sign-in",
      }),
    );
  }

  return parts.length === 0 ? t`For Windows setup at its first start` : parts.join(", ");
}

// What a Windows Update step brings besides Windows' own updates, and how many it leaves out.
function updatesDetail(node: WindowsUpdateStep): string {
  const parts = [node.microsoftProducts ? t`Windows and Microsoft products` : t`Windows only`];

  if (node.drivers) {
    parts.push(t`drivers`);
  }

  if (node.optionalUpdates) {
    parts.push(t`optional updates`);
  }

  const excluded = node.excludedKbs.length;

  if (excluded > 0) {
    parts.push(plural(excluded, { one: "# left out", other: "# left out" }));
  }

  return parts.join(", ");
}

export function nodeDetail(
  node: SequenceStep,
  {
    catalog,
    subjects,
    findings,
    accounts,
  }: {
    catalog: StepCatalog;
    subjects: readonly Subject[];
    findings: Findings;
    accounts: readonly AccountView[] | null;
  },
): NodeDetail {
  const [problem] = findings.problems;
  const plain = (text: string): NodeDetail => ({ text, code: false, problem: false });

  if (problem !== undefined) {
    return { text: findingText(problem), code: false, problem: true };
  }

  const when = node.kind === "if" ? null : legacyTree(node.conditions, node.when);

  if (when !== null && node.kind !== "repeat") {
    const summary = conditionSummary(when, subjects);

    return plain(t`Only when ${summary}`);
  }

  switch (node.kind) {
    case "partition":
      return plain(t`Erases the disk and makes the partitions`);
    case "applyImage":
    case "writeRawImage":
      return plain(imageName(catalog, node.imageId));
    case "injectDrivers":
      return plain(t`Driver packages matched to the model`);
    case "writeUnattend":
      return plain(unattendDetail(node));
    case "joinDomain":
      return plain(
        node.account === null || node.account === undefined
          ? t`With the join account of the deployment defaults`
          : accountName(accounts, node.account),
      );
    case "runScript": {
      const interpreter = interpreterLabel(node.interpreter);
      const where = inPhase(node.phase);

      return plain(t`${interpreter}, ${where}`);
    }
    case "reboot":
      return plain(t`Restarts the machine`);
    case "writeCloudInitSeed":
      return plain(t`cloud-init's seed files`);
    case "setVariable": {
      const variable = node.variable === "" ? "…" : node.variable;
      const value = node.value;

      return { text: `${variable} = ${value}`, code: true, problem: false };
    }
    case "pause":
      return plain(
        node.message.trim() === "" ? t`Waits until someone lets the run go on` : node.message,
      );
    case "installApplications":
      return plain(applicationsDetail(node.applications.length, node.variable));
    case "enableFeatures":
      return plain(
        node.features.length === 0 ? t`No features chosen yet` : node.features.join(", "),
      );
    case "windowsUpdate":
      return plain(updatesDetail(node));
    case "if":
      return plain(conditionSummary(node.test, subjects));
    case "group":
      return plain(t`Runs its steps in order`);
    case "repeat": {
      const until = conditionSummary(node.until, subjects);
      const times = node.maxTimes;

      return plain(t`Until ${until}, at most ${times} times`);
    }
  }
}
