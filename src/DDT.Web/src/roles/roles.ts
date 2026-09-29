// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api";
import type { RuleView } from "@/rules/rules";
import { editedValues, namedValues, type EditedValue, type NamedValue } from "@/values/values";

// A machine role, such as "Kiosk" or "Finance laptop". It's a set of values that rules give machines together. A user
// role is something else: it says what a person may do. ruleCount is how many rules give it.
export interface MachineRoleView {
  id: string;
  name: string;
  description: string | null;
  values: NamedValue[];
  revision: number;
  ruleCount: number;
  updatedUtc: string;
  updatedBy: string | null;
}

// revision is the one the page last read. A new role doesn't have one yet.
export interface SaveMachineRoleRequest {
  revision: number;
  name: string;
  description: string | null;
  values: NamedValue[];
}

export const machineRolesQuery = queryOptions({
  queryKey: ["machine-roles"],
  queryFn: () => apiGet<MachineRoleView[]>("/api/machine-roles"),
});

export function createRole(request: SaveMachineRoleRequest): Promise<MachineRoleView> {
  return apiPost<MachineRoleView>("/api/machine-roles", request);
}

export function updateRole(id: string, request: SaveMachineRoleRequest): Promise<MachineRoleView> {
  return apiPut<MachineRoleView>(`/api/machine-roles/${id}`, request);
}

// Refused with 409 while a rule gives the role.
export function deleteRole(id: string): Promise<void> {
  return apiDelete(`/api/machine-roles/${id}`);
}

// The server's order: by name, ignoring case.
function byName(a: MachineRoleView, b: MachineRoleView): number {
  const x = a.name.toUpperCase();
  const y = b.name.toUpperCase();

  return x < y ? -1 : x > y ? 1 : 0;
}

export function putRole(queryClient: QueryClient, role: MachineRoleView): void {
  queryClient.setQueryData(machineRolesQuery.queryKey, (list) =>
    list === undefined
      ? list
      : [...list.filter((existing) => existing.id !== role.id), role].sort(byName),
  );
}

export function removeRole(queryClient: QueryClient, id: string): void {
  queryClient.setQueryData(machineRolesQuery.queryKey, (list) =>
    list?.filter((role) => role.id !== id),
  );
}

// The rules that give a role, top first.
export function rulesGiving(rules: readonly RuleView[], roleId: string): RuleView[] {
  return rules.filter((rule) => rule.roleIds.includes(roleId));
}

// The fields a role's drawer edits, as typed.
export interface RoleEdit {
  name: string;
  description: string;
  values: EditedValue[];
}

export function roleEditOf(role: MachineRoleView | null): RoleEdit {
  return role === null
    ? { name: "", description: "", values: [] }
    : { name: role.name, description: role.description ?? "", values: editedValues(role.values) };
}

export function roleRequestOf(revision: number, edit: RoleEdit): SaveMachineRoleRequest {
  const description = edit.description.trim();

  return {
    revision,
    name: edit.name.trim(),
    description: description === "" ? null : description,
    values: namedValues(edit.values),
  };
}
