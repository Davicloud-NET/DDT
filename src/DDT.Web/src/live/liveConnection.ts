// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";

import { appendAudit, auditKey, type AuditEntry } from "@/audit/audit";
import { currentUserQuery } from "@/auth/auth";
import { bootImageQuery, type BootImageView } from "@/boot/bootImage";
import type { DeploymentStepView } from "@/deployments/deployments";
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

// The part of SignalR's HubConnection the live connection uses, so tests can hand in a fake hub. The never
// lets each handler declare the payload of its own event.
export interface LiveHub {
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>;
  on(methodName: string, handler: (payload: never) => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
}

// The server's MachineLogAppendedEvent: the machine has log lines up to this id.
export interface MachineLogAppended {
  machineId: string;
  lastLineId: number;
}

// The server's RunStepChangedEvent: a step of the machine's run changed.
export interface RunStepChanged {
  machineId: string;
  deploymentId: string;
  step: DeploymentStepView;
}

// The server sends these events only to the connections that watch the machine.
export interface MachineWatchHandlers {
  onLogAppended?: (event: MachineLogAppended) => void;
  onRunStepChanged?: (event: RunStepChanged) => void;
  // Called once the machine is watched again after the connection was lost, or first came up after the
  // watch began. Events sent meanwhile are lost, so this is when a watcher reads what it missed.
  onReconnect?: () => void;
}

// "reconnecting" while SignalR brings a dropped connection back; "offline" when there is none, including
// before the first connect and while a failed start waits to be retried.
export type LiveStatus = "live" | "reconnecting" | "offline";

export interface LiveConnection {
  start: () => void;
  stop: () => void;
  status: () => LiveStatus;
  // Returns the unsubscribe.
  onStatusChange: (listener: () => void) => () => void;
  // Returns the unsubscribe. Watchers of one machine share its group on the hub.
  watchMachine: (machineId: string, handlers: MachineWatchHandlers) => () => void;
}

function backoff(attempt: number): number {
  return Math.min(30_000, 1_000 * 2 ** attempt);
}

function buildHub(): LiveHub {
  return new HubConnectionBuilder()
    .withUrl("/hubs/live")
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (retry) => backoff(retry.previousRetryCount),
    })
    .configureLogging(LogLevel.Warning)
    .build();
}

// A refused watch, for example past the server's limit per connection, only means that no events come for
// that machine; its page still reads what it shows.
function invokeQuietly(hub: LiveHub, methodName: string, machineId: string): void {
  hub.invoke(methodName, machineId).catch(() => undefined);
}

// One connection for the signed in application. Events patch the query cache directly. Anything sent
// while disconnected is lost, so every successful connect refetches what the events would have patched.
// start and stop may alternate, as React's strict mode does; each start builds a new hub.
export function createLiveConnection(
  queryClient: QueryClient,
  build: () => LiveHub = buildHub,
): LiveConnection {
  const watchers = new Map<string, Set<{ handlers: MachineWatchHandlers }>>();
  const statusListeners = new Set<() => void>();

  let hub: LiveHub | null = null;
  let status: LiveStatus = "offline";
  let retryTimer: ReturnType<typeof setTimeout> | undefined;

  const setStatus = (next: LiveStatus) => {
    if (status !== next) {
      status = next;

      for (const listener of [...statusListeners]) {
        listener();
      }
    }
  };

  const invalidate = (queryKey: readonly unknown[]) => {
    void queryClient.invalidateQueries({ queryKey });
  };

  // What a machine would run is the server's answer to the rules and to whether the chosen sequence has problems.
  const refetchResolutions = () => {
    invalidate(sequenceResolutionsKey);
  };

  // A sequence's problems depend on the library, so an image or package that is gone reads the sequences again,
  // which their editors take without losing unsaved edits.
  const refetchSequenceProblems = () => {
    invalidate(sequencesQuery.queryKey);
    invalidate(sequenceDocumentsKey);
    refetchResolutions();
  };

  // A copy at least as new as the change, such as the one this page's own save returned, is kept.
  const sequenceChanged = (event: SequenceChanged) => {
    invalidate(sequencesQuery.queryKey);
    refetchResolutions();

    const cached = queryClient.getQueryData(sequenceQuery(event.id).queryKey);

    if (cached === undefined || event.revision === null || cached.revision < event.revision) {
      invalidate(sequenceQuery(event.id).queryKey);
    }
  };

  // An upload that finished leaves the list of unfinished ones, which only the server keeps.
  const uploadFinished = () => {
    invalidate(uploadsQuery.queryKey);
  };

  const ownUserId = () => queryClient.getQueryData(currentUserQuery.queryKey)?.id ?? null;

  const watchesOf = (machineId: string) => [...(watchers.get(machineId) ?? [])];

  // Groups do not survive a lost connection, so every watched machine is watched again before its
  // watchers read what they missed.
  // Everything the events would have patched is read once more, since what was sent while disconnected is lost.
  // Only the lists a page shows are read at once; the rest when a page next needs them.
  const connected = async (current: LiveHub) => {
    setStatus("live");

    for (const key of [
      machinesQuery.queryKey,
      imagesQuery.queryKey,
      packagesQuery.queryKey,
      uploadsQuery.queryKey,
      rulesQuery.queryKey,
      machineRolesQuery.queryKey,
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
    ]) {
      invalidate(key);
    }

    const missed = [...watchers].flatMap(([machineId, watches]) =>
      [...watches].map((watch) => ({ machineId, watch })),
    );

    await Promise.allSettled(
      [...watchers.keys()].map((machineId) => current.invoke("WatchMachine", machineId)),
    );

    for (const { machineId, watch } of missed) {
      if (hub === current && watchers.get(machineId)?.has(watch) === true) {
        watch.handlers.onReconnect?.();
      }
    }
  };

  // Automatic reconnect covers a dropped connection but not a failed first start, so that is retried here.
  const connect = async (current: LiveHub, attempt: number): Promise<void> => {
    try {
      await current.start();
    } catch {
      if (hub === current) {
        retryTimer = setTimeout(() => void connect(current, attempt + 1), backoff(attempt));
      }

      return;
    }

    if (hub === current) {
      await connected(current);
    }
  };

  const start = () => {
    if (hub !== null) {
      return;
    }

    const current = build();
    hub = current;

    current.on("machineChanged", (machine: MachineSummary) => {
      upsertMachine(queryClient, machine);
      renameMachineInRuns(queryClient, machine);
    });

    current.on("machinesRemoved", (event: MachinesRemoved) => {
      removeMachines(queryClient, event.machineIds);
      removeMachinesFromRuns(queryClient, event.machineIds);
    });

    current.on("runChanged", (item: RunHistoryItem) => {
      upsertRun(queryClient, item);
    });

    current.on("imageChanged", (image: ImageSummary) => {
      upsertImage(queryClient, image);
      uploadFinished();
    });

    current.on("imagesRemoved", (event: ImagesRemoved) => {
      removeImages(queryClient, event.imageIds);
      refetchSequenceProblems();
    });

    current.on("packageChanged", (item: PackageSummary) => {
      upsertPackage(queryClient, item);
      uploadFinished();
    });

    current.on("packagesRemoved", (event: PackagesRemoved) => {
      removePackages(queryClient, event.packageIds);
      refetchSequenceProblems();
    });

    current.on("sequenceChanged", sequenceChanged);

    // Rules are few and reorder together, so the event carries the whole ordered list.
    current.on("rulesChanged", (rules: RuleView[]) => {
      queryClient.setQueryData(rulesQuery.queryKey, rules);
      refetchResolutions();
    });

    // Machine roles too, as a change of one changes what the rules that give it do.
    current.on("rolesChanged", (roles: MachineRoleView[]) => {
      queryClient.setQueryData(machineRolesQuery.queryKey, roles);
      refetchResolutions();
    });

    current.on("bootImageChanged", (view: BootImageView) => {
      queryClient.setQueryData(bootImageQuery.queryKey, view);
    });

    // Only administrators receive these.
    current.on("auditAppended", (entries: AuditEntry[]) => {
      appendAudit(queryClient, entries);
    });

    current.on("userChanged", (user: UserView) => {
      upsertUser(queryClient, user);
    });

    current.on("usersRemoved", (event: UsersRemoved) => {
      removeUsers(queryClient, event.userIds);
      removeTokensOf(queryClient, event.userIds);
    });

    // Administrators receive every section; operators the deployment and machine sections they may read. The
    // overview counts problems and locks, so it is read again. So is what the pages read from the directory with the
    // ldap section: the group map with the names only the directory has, and the groups found by name.
    current.on("settingsChanged", (view: SettingsSectionView<unknown>) => {
      putSection(queryClient, view);
      invalidate(settingsOverviewQuery.queryKey);

      if (view.section === "ldap") {
        invalidate(directoryQuery.queryKey);
      }
    });

    // Administrators receive every pxe host's interfaces whenever a host applied the pxe section.
    current.on("pxeInterfacesChanged", (hosts: PxeHostInterfaces[]) => {
      queryClient.setQueryData(pxeInterfacesQuery.queryKey, hosts);
    });

    // Administrators receive an uploaded agent.
    current.on("agentChanged", (agent: AgentBinaryView) => {
      queryClient.setQueryData(agentBinaryQuery.queryKey, agent);
    });

    // And an uploaded console.
    current.on("consoleChanged", (console: AgentBinaryView) => {
      queryClient.setQueryData(consoleBinaryQuery.queryKey, console);
    });

    // Administrators and operators receive the console's logo when it was uploaded or removed.
    current.on("consoleLogoChanged", (logo: ConsoleLogoView) => {
      queryClient.setQueryData(consoleLogoQuery.queryKey, logo);
    });

    current.on("certificateChanged", (view: unknown) => {
      queryClient.setQueryData(certificateKey, (current: unknown) =>
        current !== null && typeof current === "object" && view !== null && typeof view === "object"
          ? { ...view, servedHere: (current as { servedHere?: unknown }).servedHere ?? null }
          : view,
      );
      // The boot image page shows the served certificate as the server endpoint describes it, in another shape.
      invalidate(serverCertificateQuery.queryKey);
    });

    // Administrators and the token's owner receive it.
    current.on("tokenChanged", (token: ApiTokenView) => {
      upsertToken(queryClient, token, ownUserId());
    });

    current.on("machineLogAppended", (event: MachineLogAppended) => {
      for (const watch of watchesOf(event.machineId)) {
        watch.handlers.onLogAppended?.(event);
      }
    });

    current.on("runStepChanged", (event: RunStepChanged) => {
      for (const watch of watchesOf(event.machineId)) {
        watch.handlers.onRunStepChanged?.(event);
      }
    });

    current.onreconnecting(() => {
      if (hub === current) {
        setStatus("reconnecting");
      }
    });

    current.onreconnected(() => {
      if (hub === current) {
        void connected(current);
      }
    });

    current.onclose(() => {
      if (hub === current) {
        setStatus("offline");
        void connect(current, 0);
      }
    });

    void connect(current, 0);
  };

  const stop = () => {
    const current = hub;

    if (current === null) {
      return;
    }

    hub = null;
    clearTimeout(retryTimer);
    setStatus("offline");
    void current.stop();
  };

  const watchMachine = (machineId: string, handlers: MachineWatchHandlers) => {
    const watch = { handlers };
    let watches = watchers.get(machineId);

    if (watches === undefined) {
      watches = new Set();
      watchers.set(machineId, watches);

      if (hub !== null && status === "live") {
        invokeQuietly(hub, "WatchMachine", machineId);
      }
    }

    watches.add(watch);
    const own = watches;

    return () => {
      if (!own.delete(watch) || own.size > 0) {
        return;
      }

      watchers.delete(machineId);

      if (hub !== null && status === "live") {
        invokeQuietly(hub, "UnwatchMachine", machineId);
      }
    };
  };

  return {
    start,
    stop,
    status: () => status,
    onStatusChange: (listener) => {
      statusListeners.add(listener);

      return () => {
        statusListeners.delete(listener);
      };
    },
    watchMachine,
  };
}
