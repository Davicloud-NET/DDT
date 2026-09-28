// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";
import { queryOptions } from "@tanstack/react-query";

import { apiGet, apiPost } from "@/lib/api";

import { settingsKey, type SettingsOverview, type SettingsSectionView } from "./settings";
import type { SettingsForm } from "./useSettingsForm";

// The pxe settings section, as GET /api/settings/pxe returns it: which interfaces DDT answers netboot on, how ProxyDHCP
// and TFTP behave, and the boot file each client architecture gets.
export interface PxeSettings {
  interfaces: string[];
  enableProxyDhcp: boolean;
  enableTftp: boolean;
  tftpSinglePort: boolean;
  tftpMaxWindowSize: number;
  maxConcurrentTftpTransfers: number;
  authorisedRelayAgents: string[];
  // Keyed by client architecture, such as X64Uefi.
  bootTargets: Record<string, BootTargetSettings>;
}

export type PxeForm = SettingsForm<PxeSettings>;

export interface BootTargetSettings {
  method: string | null;
  bootFile: string | null;
  serverAddress: string | null;
  serverHostName: string | null;
  advertiseBootServerDiscovery: boolean;
}

// What a host that runs network boot found when it last applied the section. served says whether it answers on the
// interface now. unmatched lists the entries that match nothing on that host.
export interface PxeInterface {
  name: string;
  addresses: string[];
  served: boolean;
}

export interface PxeHostInterfaces {
  host: string;
  updatedUtc: string | null;
  interfaces: PxeInterface[];
  unmatched: string[];
}

// The hosts report their interfaces when they apply the section. The hub's pxeInterfacesChanged then carries the whole
// list. The key is inside ["settings"], so it's read again when the live connection reconnects.
export const pxeInterfacesQuery = queryOptions({
  queryKey: [...settingsKey("pxe"), "interfaces"],
  queryFn: () => apiGet<PxeHostInterfaces[]>("/api/settings/pxe/interfaces"),
});

// HttpBootPort and BootDirectory stay in configuration, so they come from the overview's server values. They only
// change with a restart, which a reconnect notices. So they're read once, not with the overview on every push.
export interface PxeConfiguration {
  httpBootPort: number | null;
  bootDirectory: string | null;
}

export const pxeConfigurationQuery = queryOptions({
  queryKey: [...settingsKey("pxe"), "configuration"],
  queryFn: async (): Promise<PxeConfiguration> => {
    const overview = await apiGet<SettingsOverview>("/api/settings");
    const value = (key: string) =>
      overview.server.find((setting) => setting.key === key)?.value ?? null;
    const port = Number.parseInt(value("DDT:Pxe:HttpBootPort") ?? "", 10);

    return {
      httpBootPort: Number.isNaN(port) ? null : port,
      bootDirectory: value("DDT:Pxe:BootDirectory"),
    };
  },
  staleTime: Infinity,
});

// Saves the section unchanged with a new version, so every host scans its interfaces and applies it again. The answer
// is the section once this host has had a moment to apply it. Other hosts may still be rescanning.
export function rescanPxe(): Promise<SettingsSectionView<PxeSettings>> {
  return apiPost<SettingsSectionView<PxeSettings>>("/api/settings/pxe/rescan");
}

// The member names of ClientArchitecture (src/DDT.Protocols/Dhcp/ClientArchitecture.cs), in the order of the IANA
// registry for DHCP option 93. The server matches a boot target's key against them, ignoring case.
export const clientArchitectures = [
  "X86Bios",
  "Nec98",
  "Itanium",
  "DecAlpha",
  "ArcX86",
  "IntelLeanClient",
  "X86Uefi",
  "X64Uefi",
  "EfiXscale",
  "Ebc",
  "Arm32Uefi",
  "Arm64Uefi",
  "PowerPcOpenFirmware",
  "PowerPcEpapr",
  "PowerOpalV3",
  "X86UefiHttp",
  "X64UefiHttp",
  "EbcHttp",
  "Arm32UefiHttp",
  "Arm64UefiHttp",
  "PcAtBiosHttp",
  "Arm32Uboot",
  "Arm64Uboot",
  "Arm32UbootHttp",
  "Arm64UbootHttp",
  "RiscV32Uefi",
  "RiscV32UefiHttp",
  "RiscV64Uefi",
  "RiscV64UefiHttp",
  "RiscV128Uefi",
  "RiscV128UefiHttp",
  "S390Basic",
  "S390Extended",
  "Mips32Uefi",
  "Mips64Uefi",
  "Sunway32Uefi",
  "Sunway64Uefi",
  "LoongArch32Uefi",
  "LoongArch32UefiHttp",
  "LoongArch64Uefi",
  "LoongArch64UefiHttp",
  "ArmRpiBoot",
] as const;

// The architectures PCs report are offered first, with a description. The rest follow in the registry's order.
const described: Partial<Record<string, MessageDescriptor>> = {
  X64Uefi: msg`64-bit UEFI PCs, over TFTP`,
  X64UefiHttp: msg`64-bit UEFI PCs, over HTTP`,
  Arm64Uefi: msg`64-bit Arm UEFI, over TFTP`,
  Arm64UefiHttp: msg`64-bit Arm UEFI, over HTTP`,
  X86Uefi: msg`32-bit UEFI, over TFTP`,
  X86UefiHttp: msg`32-bit UEFI, over HTTP`,
  X86Bios: msg`BIOS PCs, over TFTP`,
};

export const architecturesInOrder: readonly string[] = [
  ...Object.keys(described),
  ...clientArchitectures.filter((name) => !(name in described)),
];

export function architectureDescription(name: string): MessageDescriptor | null {
  return described[canonicalArchitecture(name) ?? ""] ?? null;
}

// The member name a key stands for, or null if the key isn't an architecture.
export function canonicalArchitecture(key: string): string | null {
  const lower = key.trim().toLowerCase();

  return clientArchitectures.find((name) => name.toLowerCase() === lower) ?? null;
}

// The architecture decides the method. HTTP boot firmware only accepts a URL, and PXE firmware only a TFTP path, so the
// server refuses any other combination.
export function methodFor(architecture: string): "Tftp" | "Http" {
  return architecture.endsWith("Http") ? "Http" : "Tftp";
}

// The two boot managers the boot image layout holds (build/Build-BootImage.ps1).
export const bootManagers = [
  { path: "x64/bootmgfw.efi", authority: "2011" },
  { path: "x64/bootmgfw_ex.efi", authority: "2023" },
] as const;

export const defaultBootManager = bootManagers[0].path;

// DDT serves boot files over plain HTTP on its boot port, under /boot/, because firmware can't check a private CA.
export function bootUrl(server: string, port: number, path: string): string {
  return `http://${server}:${String(port)}/boot/${path}`;
}

// The host of an http or https URL, or null for anything else.
export function hostOf(value: string | null): string | null {
  if (value === null) {
    return null;
  }

  try {
    const url = new URL(value);

    return url.protocol === "http:" || url.protocol === "https:" ? url.hostname : null;
  } catch {
    return null;
  }
}

// Whether an entry names an interface, the way the host matches it: by name, ignoring case, or by an exact match on one
// of its addresses.
function names(entry: string, candidate: PxeInterface): boolean {
  return (
    entry.toLowerCase() === candidate.name.toLowerCase() || candidate.addresses.includes(entry)
  );
}

export function isListed(entries: readonly string[], candidate: PxeInterface): boolean {
  return entries.some((entry) => names(entry, candidate));
}

// Serving an interface adds its name to the list. Not serving it removes its name and all of its addresses.
export function withInterface(
  entries: readonly string[],
  candidate: PxeInterface,
  serve: boolean,
): string[] {
  if (serve) {
    return isListed(entries, candidate) ? [...entries] : [...entries, candidate.name];
  }

  return entries.filter((entry) => !names(entry, candidate));
}

// The entries that aren't the name of an interface a host reported, such as an address or a future adapter.
export function otherEntries(
  entries: readonly string[],
  hosts: readonly PxeHostInterfaces[],
): string[] {
  const reported = new Set(
    hosts.flatMap((host) => host.interfaces.map((candidate) => candidate.name.toLowerCase())),
  );

  return entries.filter((entry) => !reported.has(entry.toLowerCase()));
}

// Adds typed entries to the section's list. Several can be typed at once, separated by commas. An entry that's already
// listed is skipped.
export function withEntries(entries: readonly string[], typed: string): string[] {
  const next = [...entries];

  for (const entry of typed.split(",").map((part) => part.trim())) {
    if (entry !== "" && !next.some((listed) => listed.toLowerCase() === entry.toLowerCase())) {
      next.push(entry);
    }
  }

  return next;
}
