// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n, type MessageDescriptor } from "@lingui/core";
import { msg, t } from "@lingui/core/macro";

import { formattingLocale } from "@/i18n/i18n";
import { formatBytes } from "@/lib/format";

import type { MachineFacts, MachineSummary } from "./machines";

// The names of what a machine reports, as conditions, rules and the machine's page say them: the server's
// MachineVariableNames.Catalogue, whose names a condition tests, in the order a page lists them. The flow builder's
// condition builder and a run's decisions take their labels from here.

const factLabels: Record<string, MessageDescriptor> = {
  Manufacturer: msg`Manufacturer`,
  Model: msg`Model`,
  FriendlyModel: msg`Friendly model`,
  SerialNumber: msg`Serial number`,
  SmbiosUuid: msg`SMBIOS UUID`,
  DeviceKind: msg`Device kind`,
  MacAddress: msg`MAC address`,
  PrimaryMacAddress: msg`Primary MAC address`,
  ComputerName: msg`Computer name`,
  Phase: msg`Phase`,
  MemoryMegabytes: msg`Memory`,
  ProcessorName: msg`Processor`,
  ProcessorCores: msg`Processor cores`,
  LogicalProcessors: msg`Logical processors`,
  TpmPresent: msg`TPM present`,
  TpmVersion: msg`TPM version`,
  SecureBootCapable: msg`Secure Boot capable`,
  SecureBootEnabled: msg`Secure Boot on`,
  IPv4Address: msg`IPv4 address`,
  IPv4PrefixLength: msg`Network prefix length`,
  Subnet: msg`Subnet`,
  DefaultGateway: msg`Default gateway`,
  DnsSuffix: msg`DNS suffix`,
  DhcpServer: msg`DHCP server`,
  SystemVersion: msg`System version`,
  SystemFamily: msg`System family`,
  SystemSku: msg`System SKU`,
  AssetTag: msg`Asset tag`,
  BaseboardProduct: msg`Baseboard`,
  BiosVersion: msg`BIOS version`,
  BiosDate: msg`BIOS date`,
  LastStepFailed: msg`Last step failed`,
  LastExitCode: msg`Last exit code`,
};

// The catalogue's names, in its order.
export const factNames = Object.keys(factLabels);

// A fact's label, or the name as it is for a name that is not a fact, such as a sequence's own variable.
export function factLabel(name: string): string {
  const key = factNames.find((candidate) => candidate.toLowerCase() === name.toLowerCase());
  const descriptor = key === undefined ? undefined : factLabels[key];

  return descriptor === undefined ? name : i18n._(descriptor);
}

// Whether a name is one of the catalogue's, which the machine or the run reports, rather than a value a sequence, a
// rule or a machine role sets. Names ignore case.
export function isFact(name: string): boolean {
  return factNames.some((candidate) => candidate.toLowerCase() === name.toLowerCase());
}

// The run's own values, which change while it goes on.
export function isRunVariable(name: string): boolean {
  return ["laststepfailed", "lastexitcode"].includes(name.toLowerCase());
}

const deviceKinds: Record<string, MessageDescriptor> = {
  Laptop: msg`Laptop`,
  Desktop: msg`Desktop`,
  Tablet: msg`Tablet`,
  Server: msg`Server`,
  Virtual: msg`Virtual machine`,
  Unknown: msg`Unknown`,
};

// A fact's value as a page says it: yes or no, memory as a size, a device kind in words.
export function factValueText(name: string, value: string): string {
  const key = factNames.find((candidate) => candidate.toLowerCase() === name.toLowerCase());
  const lower = value.trim().toLowerCase();

  switch (key) {
    case "TpmPresent":
    case "SecureBootCapable":
    case "SecureBootEnabled":
    case "LastStepFailed":
      return lower === "true" ? t`Yes` : lower === "false" ? t`No` : value;
    case "MemoryMegabytes": {
      const megabytes = Number(value);

      return Number.isFinite(megabytes) && value.trim() !== ""
        ? formatBytes(megabytes * 1024 * 1024)
        : value;
    }
    case "DeviceKind": {
      const kind = Object.keys(deviceKinds).find((candidate) => candidate.toLowerCase() === lower);
      const descriptor = kind === undefined ? undefined : deviceKinds[kind];

      return descriptor === undefined ? value : i18n._(descriptor);
    }
    default:
      return value;
  }
}

export interface FactRow {
  name: string;
  label: string;
  value: string;
  // Identifiers such as addresses read better in the mono face.
  mono: boolean;
}

function yesNo(value: boolean): string {
  return value ? t`Yes` : t`No`;
}

// What the machine reported besides its identity, which the machine's header already shows, as rows in the
// catalogue's order: only what it reported.
export function factRows(machine: Pick<MachineSummary, "facts">): FactRow[] {
  const facts: MachineFacts = machine.facts ?? {};
  const rows: FactRow[] = [];
  const add = (name: string, value: string | null | undefined, mono = false) => {
    if (value !== null && value !== undefined && value !== "") {
      rows.push({ name, label: factLabel(name), value, mono });
    }
  };
  const count = (value: number | null | undefined) =>
    value === null || value === undefined
      ? null
      : new Intl.NumberFormat(formattingLocale()).format(value);
  const address = facts.iPv4Address ?? null;
  const prefix = facts.iPv4PrefixLength ?? null;

  add(
    "MemoryMegabytes",
    facts.memoryMegabytes === null || facts.memoryMegabytes === undefined
      ? null
      : formatBytes(facts.memoryMegabytes * 1024 * 1024),
  );
  add("ProcessorName", facts.processorName);
  add("ProcessorCores", count(facts.processorCores));
  add("LogicalProcessors", count(facts.logicalProcessors));
  add(
    "TpmPresent",
    facts.tpmPresent === null || facts.tpmPresent === undefined ? null : yesNo(facts.tpmPresent),
  );
  add("TpmVersion", facts.tpmVersion);
  add(
    "SecureBootCapable",
    facts.secureBootCapable === null || facts.secureBootCapable === undefined
      ? null
      : yesNo(facts.secureBootCapable),
  );
  add(
    "IPv4Address",
    address === null ? null : prefix === null ? address : `${address}/${String(prefix)}`,
    true,
  );
  add("DefaultGateway", facts.defaultGateway, true);
  add("DnsSuffix", facts.dnsSuffix, true);
  add("DhcpServer", facts.dhcpServer, true);
  add("SystemVersion", facts.systemVersion);
  add("SystemFamily", facts.systemFamily);
  add("SystemSku", facts.systemSku, true);
  add("AssetTag", facts.assetTag, true);
  add("BaseboardProduct", facts.baseboardProduct);
  add("BiosVersion", facts.biosVersion, true);
  add("BiosDate", facts.biosDate === null || facts.biosDate === undefined ? null : biosDate(facts));

  return rows;
}

// The BIOS date comes as yyyy-MM-dd and is shown as a date in the person's language.
function biosDate(facts: MachineFacts): string | null {
  const text = facts.biosDate ?? null;

  if (text === null || !/^\d{4}-\d{2}-\d{2}$/.test(text)) {
    return text;
  }

  return new Date(`${text}T00:00:00Z`).toLocaleDateString(formattingLocale(), {
    timeZone: "UTC",
    dateStyle: "medium",
  });
}
