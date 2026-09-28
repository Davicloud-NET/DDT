// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { QueryClient, QueryKey } from "@tanstack/react-query";

import {
  accountsQuery,
  putAccount,
  removeAccounts,
  type AccountsRemoved,
  type AccountView,
} from "@/accounts/accounts";
import { appendAudit, auditKey, type AuditEntry } from "@/audit/audit";
import { currentUserQuery } from "@/auth/auth";
import { bootImageQuery, type BootImageView } from "@/boot/bootImage";
import {
  imagesQuery,
  removeImages,
  upsertImage,
  uploadsQuery,
  type ImageSummary,
  type ImagesRemoved,
} from "@/images/images";
import {
  machinesQuery,
  removeMachines,
  upsertMachine,
  type MachinesRemoved,
  type MachineSummary,
} from "@/machines/machines";
import {
  packagesQuery,
  removePackages,
  upsertPackage,
  type PackageSummary,
  type PackagesRemoved,
} from "@/packages/packages";
import { machineRolesQuery, type MachineRoleView } from "@/roles/roles";
import { rulesQuery, sequenceResolutionsKey, type RuleView } from "@/rules/rules";
import {
  removeMachinesFromRuns,
  renameMachineInRuns,
  runHistoryKey,
  upsertRun,
  type RunHistoryItem,
} from "@/runs/runHistory";
import {
  sequenceDocumentsKey,
  sequenceQuery,
  sequencesQuery,
  type SequenceChanged,
} from "@/sequences/sequences";
import {
  certificateKey,
  putSection,
  settingsOverviewQuery,
  type SettingsSectionView,
} from "@/settings/settings";
import { agentBinaryQuery, consoleBinaryQuery, type AgentBinaryView } from "@/settings/agentBinary";
import { consoleLogoQuery, type ConsoleLogoView } from "@/settings/consoleLogo";
import { serverCertificateQuery } from "@/server/serverCertificate";
import { pxeInterfacesQuery, type PxeHostInterfaces } from "@/settings/networkBoot";
import { removeTokensOf, upsertToken, type ApiTokenView } from "@/tokens/tokens";
import {
  directoryQuery,
  removeUsers,
  upsertUser,
  usersQuery,
  type UsersRemoved,
  type UserView,
} from "@/users/users";

import type { EventHandler } from "./liveConnection";

function invalidate(queryClient: QueryClient, queryKey: QueryKey): void {
  void queryClient.invalidateQueries({ queryKey });
}

// What a machine would run is the server's answer to the rules and to whether the chosen sequence has problems.
function refetchResolutions(queryClient: QueryClient): void {
  invalidate(queryClient, sequenceResolutionsKey);
}

// A sequence's problems depend on the library, so an image or package that is gone reads the sequences again,
// which their editors take without losing unsaved edits.
function refetchSequenceProblems(queryClient: QueryClient): void {
  invalidate(queryClient, sequencesQuery.queryKey);
  invalidate(queryClient, sequenceDocumentsKey);
  refetchResolutions(queryClient);
}

// An upload that finished leaves the list of unfinished ones, which only the server keeps.
function uploadFinished(queryClient: QueryClient): void {
  invalidate(queryClient, uploadsQuery.queryKey);
}

// A copy at least as new as the change, such as the one this page's own save returned, is kept.
function sequenceChanged(queryClient: QueryClient, event: SequenceChanged): void {
  invalidate(queryClient, sequencesQuery.queryKey);
  refetchResolutions(queryClient);

  const cached = queryClient.getQueryData(sequenceQuery(event.id).queryKey);

  if (cached === undefined || event.revision === null || cached.revision < event.revision) {
    invalidate(queryClient, sequenceQuery(event.id).queryKey);
  }
}

function machineEvents(queryClient: QueryClient) {
  return {
    machineChanged: (machine: MachineSummary) => {
      upsertMachine(queryClient, machine);
      renameMachineInRuns(queryClient, machine);
    },
    machinesRemoved: (event: MachinesRemoved) => {
      removeMachines(queryClient, event.machineIds);
      removeMachinesFromRuns(queryClient, event.machineIds);
    },
    runChanged: (item: RunHistoryItem) => {
      upsertRun(queryClient, item);
    },
  };
}

function libraryEvents(queryClient: QueryClient) {
  return {
    imageChanged: (image: ImageSummary) => {
      upsertImage(queryClient, image);
      uploadFinished(queryClient);
    },
    imagesRemoved: (event: ImagesRemoved) => {
      removeImages(queryClient, event.imageIds);
      refetchSequenceProblems(queryClient);
    },
    packageChanged: (item: PackageSummary) => {
      upsertPackage(queryClient, item);
      uploadFinished(queryClient);
    },
    packagesRemoved: (event: PackagesRemoved) => {
      removePackages(queryClient, event.packageIds);
      refetchSequenceProblems(queryClient);
    },
    sequenceChanged: (event: SequenceChanged) => {
      sequenceChanged(queryClient, event);
    },
    // Rules are few and reorder together, so the event carries the whole ordered list.
    rulesChanged: (rules: RuleView[]) => {
      queryClient.setQueryData(rulesQuery.queryKey, rules);
      refetchResolutions(queryClient);
    },
    // Machine roles too, as a change of one changes what the rules that give it do.
    rolesChanged: (roles: MachineRoleView[]) => {
      queryClient.setQueryData(machineRolesQuery.queryKey, roles);
      refetchResolutions(queryClient);
    },
    // An account carries no password, only whether one is set, and comes again when a sequence starts or stops
    // naming it.
    accountChanged: (account: AccountView) => {
      putAccount(queryClient, account);
    },
    accountsRemoved: (event: AccountsRemoved) => {
      removeAccounts(queryClient, event.accountIds);
    },
    bootImageChanged: (view: BootImageView) => {
      queryClient.setQueryData(bootImageQuery.queryKey, view);
    },
  };
}

function administrationEvents(queryClient: QueryClient) {
  const ownUserId = () => queryClient.getQueryData(currentUserQuery.queryKey)?.id ?? null;

  return {
    // Only administrators receive the audit.
    auditAppended: (entries: AuditEntry[]) => {
      appendAudit(queryClient, entries);
    },
    userChanged: (user: UserView) => {
      upsertUser(queryClient, user);
    },
    usersRemoved: (event: UsersRemoved) => {
      removeUsers(queryClient, event.userIds);
      removeTokensOf(queryClient, event.userIds);
    },
    // Administrators get every section, operators the deployment and machine ones. The overview counts problems and
    // locks, so it is read again, and so is the directory after the ldap section changed: only it knows group names.
    settingsChanged: (view: SettingsSectionView<unknown>) => {
      putSection(queryClient, view);
      invalidate(queryClient, settingsOverviewQuery.queryKey);

      if (view.section === "ldap") {
        invalidate(queryClient, directoryQuery.queryKey);
      }
    },
    // Administrators receive every pxe host's interfaces whenever a host applied the pxe section.
    pxeInterfacesChanged: (hosts: PxeHostInterfaces[]) => {
      queryClient.setQueryData(pxeInterfacesQuery.queryKey, hosts);
    },
    // Administrators receive an uploaded agent.
    agentChanged: (agent: AgentBinaryView) => {
      queryClient.setQueryData(agentBinaryQuery.queryKey, agent);
    },
    // Administrators receive an uploaded console as well.
    consoleChanged: (console: AgentBinaryView) => {
      queryClient.setQueryData(consoleBinaryQuery.queryKey, console);
    },
    // Administrators and operators receive the console's logo when it was uploaded or removed.
    consoleLogoChanged: (logo: ConsoleLogoView) => {
      queryClient.setQueryData(consoleLogoQuery.queryKey, logo);
    },
    certificateChanged: (view: unknown) => {
      queryClient.setQueryData(certificateKey, (current: unknown) =>
        current !== null && typeof current === "object" && view !== null && typeof view === "object"
          ? { ...view, servedHere: (current as { servedHere?: unknown }).servedHere ?? null }
          : view,
      );
      // The boot image page shows the served certificate as the server endpoint describes it, in another shape.
      invalidate(queryClient, serverCertificateQuery.queryKey);
    },
    // Administrators and the token's owner receive it.
    tokenChanged: (token: ApiTokenView) => {
      upsertToken(queryClient, token, ownUserId());
    },
  };
}

// The hub's events that patch the query cache, each with its handler.
export function cacheEventHandlers(queryClient: QueryClient) {
  return {
    ...machineEvents(queryClient),
    ...libraryEvents(queryClient),
    ...administrationEvents(queryClient),
  } satisfies Record<string, EventHandler>;
}

// Everything the events would have patched is read once more, since what was sent while disconnected is lost.
// Only the lists a page shows are read at once; the rest when a page next needs them.
export function reconnectKeys(): QueryKey[] {
  return [
    machinesQuery.queryKey,
    imagesQuery.queryKey,
    packagesQuery.queryKey,
    uploadsQuery.queryKey,
    rulesQuery.queryKey,
    machineRolesQuery.queryKey,
    accountsQuery.queryKey,
    sequencesQuery.queryKey,
    sequenceDocumentsKey,
    sequenceResolutionsKey,
    runHistoryKey,
    auditKey,
    usersQuery.queryKey,
    directoryQuery.queryKey,
    ["tokens"],
    bootImageQuery.queryKey,
    ["settings"],
    settingsOverviewQuery.queryKey,
    certificateKey,
    agentBinaryQuery.queryKey,
    consoleBinaryQuery.queryKey,
    consoleLogoQuery.queryKey,
    serverCertificateQuery.queryKey,
  ];
}
