// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { AccountView } from "@/accounts/accounts";
import type { MachineRoleView } from "@/roles/roles";
import type { MachineSequenceResolution, RuleView } from "@/rules/rules";
import {
  accountView,
  machineRole,
  machineSummary,
  ruleView,
  sequenceSummary,
} from "@/test/builders";

// The rules, machine roles and accounts of an office with a kiosk in its lobby, a Berlin and a Hamburg office, Dell
// laptops and virtual machines in its lab, as the design of the Rules page shows them.

const kioskRole = machineRole({
  id: "0193a4b2-0000-7000-8000-0000000000c1",
  name: "Kiosk",
  description: "The screens in the lobby, signed in as a guest.",
  values: [
    { name: "ComputerName", value: "KIOSK-{{SerialNumber|alnum|right:9}}" },
    { name: "TimeZone", value: "W. Europe Standard Time" },
  ],
  ruleCount: 1,
});

const officeRole = machineRole({
  id: "0193a4b2-0000-7000-8000-0000000000c2",
  name: "Office PC",
  description: "Desks in the offices, joined to the domain.",
  values: [{ name: "ComputerName", value: "PC-{{SerialNumber|alnum|right:12}}" }],
  ruleCount: 1,
});

export const roles: MachineRoleView[] = [kioskRole, officeRole];

const kiosk = sequenceSummary({
  id: "0193a4b2-0000-7000-8000-0000000000e2",
  name: "Kiosk",
});
const office = sequenceSummary({
  id: "0193a4b2-0000-7000-8000-0000000000e3",
  name: "Windows 11 office PCs",
});
const lab = sequenceSummary({ id: "0193a4b2-0000-7000-8000-0000000000e4", name: "Lab VM" });

export const sequences = [kiosk, office, lab];

const ou = (city: string) => `OU=${city},OU=Computers,DC=corp,DC=example`;

export const rules: RuleView[] = [
  ruleView({
    id: "0193a4b2-0000-7000-8000-0000000000f1",
    position: 0,
    name: "Kiosk in the lobby",
    when: { kind: "test", variable: "MacAddress", operator: "Equals", value: "3C:52:82:6A:1F:0B" },
    sequenceId: kiosk.id,
    sequenceName: kiosk.name,
    roleIds: [kioskRole.id],
    matchingMachines: 1,
  }),
  ruleView({
    id: "0193a4b2-0000-7000-8000-0000000000f2",
    position: 1,
    name: "Berlin office",
    when: {
      kind: "any",
      parts: [
        { kind: "test", variable: "IPv4Address", operator: "InSubnet", value: "10.20.0.0/16" },
        { kind: "test", variable: "DefaultGateway", operator: "Equals", value: "10.21.0.1" },
      ],
    },
    sequenceId: null,
    sequenceName: null,
    values: [
      { name: "TimeZone", value: "W. Europe Standard Time" },
      { name: "OrganizationalUnit", value: ou("Berlin") },
    ],
    roleIds: [officeRole.id],
    matchingMachines: 34,
  }),
  ruleView({
    id: "0193a4b2-0000-7000-8000-0000000000f3",
    position: 2,
    name: "Hamburg office",
    when: { kind: "test", variable: "DefaultGateway", operator: "Equals", value: "10.30.0.1" },
    sequenceId: null,
    sequenceName: null,
    values: [
      { name: "TimeZone", value: "W. Europe Standard Time" },
      { name: "OrganizationalUnit", value: ou("Hamburg") },
    ],
    matchingMachines: 21,
  }),
  ruleView({
    id: "0193a4b2-0000-7000-8000-0000000000f4",
    position: 3,
    name: "Latitude laptops",
    sequenceId: office.id,
    sequenceName: office.name,
    values: [{ name: "OrganizationalUnit", value: ou("Laptops") }],
    matchingMachines: 58,
  }),
  ruleView({
    id: "0193a4b2-0000-7000-8000-0000000000f5",
    position: 4,
    name: "Virtual machines",
    enabled: false,
    when: { kind: "test", variable: "DeviceKind", operator: "Equals", value: "Virtual" },
    sequenceId: lab.id,
    sequenceName: lab.name,
    matchingMachines: 0,
  }),
];

export const testedMachine = machineSummary({
  id: "0193a4b2-0000-7000-8000-000000000011",
  state: "Pending",
  assignedName: "PC-G2341KXQ",
  manufacturer: "Dell Inc.",
  model: "Latitude 7450",
  serialNumber: "G2341KXQ",
  deviceKind: "Laptop",
});

// What the server works out for the Latitude in Berlin: rules 2 and 4 match, rule 4 chooses the sequence, and rule 2's
// organizational unit wins over rule 4's.
export const testedResolution: MachineSequenceResolution = {
  source: "Rule",
  sequenceId: office.id,
  sequenceName: office.name,
  ruleId: "0193a4b2-0000-7000-8000-0000000000f4",
  problemCount: 0,
  explanation: "Rule 4, Latitude laptops, chooses Windows 11 office PCs.",
  explanationCode: "resolution.ruleNumbered",
  explanationArgs: { number: 4, rule: "Latitude laptops", sequence: office.name },
  matchedRuleIds: ["0193a4b2-0000-7000-8000-0000000000f2", "0193a4b2-0000-7000-8000-0000000000f4"],
  values: [
    {
      name: "TimeZone",
      value: "W. Europe Standard Time",
      source: "Rule",
      sourceId: "0193a4b2-0000-7000-8000-0000000000f2",
      sourceName: "Berlin office",
      overridden: false,
    },
    {
      name: "OrganizationalUnit",
      value: ou("Berlin"),
      source: "Rule",
      sourceId: "0193a4b2-0000-7000-8000-0000000000f2",
      sourceName: "Berlin office",
      overridden: false,
    },
    {
      name: "OrganizationalUnit",
      value: ou("Laptops"),
      source: "Rule",
      sourceId: "0193a4b2-0000-7000-8000-0000000000f4",
      sourceName: "Latitude laptops",
      overridden: true,
    },
    {
      name: "ComputerName",
      value: "PC-G2341KXQ",
      source: "Role",
      sourceId: officeRole.id,
      sourceName: officeRole.name,
      overridden: false,
    },
  ],
  valueProblems: [],
};

export const accounts: AccountView[] = [
  accountView({
    id: "0193a4b2-0000-7000-8000-0000000000a1",
    name: "Join account",
    userName: "CORP\\ddt-join",
    domain: "corp.example",
    usedBy: [
      {
        sequenceId: office.id,
        sequenceName: office.name,
        steps: [{ stepId: "join", stepName: "Join the domain", field: "account" }],
      },
    ],
  }),
  accountView({
    id: "0193a4b2-0000-7000-8000-0000000000a2",
    name: "Software share",
    userName: "svc-software@corp.example",
    domain: null,
    hosts: ["files.corp.example", "files-hh.corp.example"],
    runAs: true,
    usedBy: [
      {
        sequenceId: office.id,
        sequenceName: office.name,
        steps: [
          { stepId: "office", stepName: "Install Office", field: "runAs" },
          { stepId: "office", stepName: "Install Office", field: "shares[0].account" },
        ],
      },
      {
        sequenceId: kiosk.id,
        sequenceName: kiosk.name,
        steps: [{ stepId: "browser", stepName: "Install the kiosk browser", field: "runAs" }],
      },
    ],
  }),
  accountView({
    id: "0193a4b2-0000-7000-8000-0000000000a3",
    name: "Old backup account",
    userName: "CORP\\backup",
    domain: null,
    hosts: ["backup.corp.example"],
    password: { isSet: true, unreadable: true, updatedUtc: "2026-03-02T09:00:00Z" },
  }),
  accountView({
    id: "0193a4b2-0000-7000-8000-0000000000a4",
    name: "Test lab",
    userName: "LAB\\tester",
    domain: "lab.corp.example",
    runAs: true,
    password: { isSet: false, unreadable: false, updatedUtc: null },
  }),
];
