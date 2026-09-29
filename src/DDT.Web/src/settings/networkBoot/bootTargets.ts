// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  architecturesInOrder,
  bootManagers,
  bootUrl,
  canonicalArchitecture,
  defaultBootManager,
  methodFor,
  type BootTargetSettings,
  type PxeHostInterfaces,
} from "../networkBoot";

// A key that isn't an architecture sorts after all architectures.
function order(key: string): number {
  const index = architecturesInOrder.indexOf(canonicalArchitecture(key) ?? "");

  return index < 0 ? architecturesInOrder.length : index;
}

export function targetKeysInOrder(targets: Record<string, BootTargetSettings>): string[] {
  return Object.keys(targets).sort((a, b) => order(a) - order(b) || a.localeCompare(b, "en"));
}

// The server matches keys ignoring case, so an architecture already has a target if its key exists in any case.
export function architecturesWithout(keys: readonly string[]): string[] {
  const used = new Set(keys.map((key) => key.toLowerCase()));

  return architecturesInOrder.filter((name) => !used.has(name.toLowerCase()));
}

// Only the x64 boot managers are in the boot image layout, and an HTTP boot file needs the boot port for its URL.
export function newBootTarget(
  architecture: string,
  host: string,
  port: number | null,
): BootTargetSettings {
  const method = methodFor(architecture);

  return {
    method,
    bootFile: architecture.startsWith("X64") ? defaultBootFile(method, host, port) : null,
    serverAddress: null,
    serverHostName: null,
    advertiseBootServerDiscovery: false,
  };
}

function defaultBootFile(
  method: "Tftp" | "Http",
  host: string,
  port: number | null,
): string | null {
  if (method === "Tftp") {
    return defaultBootManager;
  }

  return port === null ? null : bootUrl(host, port, defaultBootManager);
}

export function withoutTarget(
  targets: Record<string, BootTargetSettings>,
  key: string,
): Record<string, BootTargetSettings> {
  return Object.fromEntries(Object.entries(targets).filter(([other]) => other !== key));
}

// The boot managers as TFTP paths, or as URLs on the boot port. No URLs are offered while the port is unknown.
export function bootFileOptions(
  http: boolean,
  server: string,
  port: number | null,
): { file: string; authority: string }[] {
  return bootManagers.flatMap((manager) => {
    const file = http ? (port === null ? null : bootUrl(server, port, manager.path)) : manager.path;

    return file === null ? [] : [{ file, authority: manager.authority }];
  });
}

// This page's host name, then the addresses of the interfaces the hosts serve. A machine can reach an address
// without a name lookup.
export function serverNameOptions(here: string, hosts: readonly PxeHostInterfaces[]): string[] {
  const addresses = hosts.flatMap((host) =>
    host.interfaces
      .filter((candidate) => candidate.served)
      .flatMap((candidate) => candidate.addresses),
  );

  return [...new Set([here, ...addresses])];
}
