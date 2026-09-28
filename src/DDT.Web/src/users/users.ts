// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPatchJson, apiPost } from "@/lib/api";
import { removeByIds, upsertById } from "@/lib/listCache";
import { serverText, type ServerArguments } from "@/lib/serverText";

export type UserRole = "Administrator" | "Operator" | "Viewer";
export type AccountSource = "Local" | "Directory" | "External";

// Where an account's role comes from: an administrator, its directory or single sign-on groups at each sign-in, or
// DDT when single sign-on created the account.
export type RoleFrom = "Manual" | "DirectoryGroups" | "SingleSignOnGroups" | "Provisioned";

export interface UserView {
  id: string;
  userName: string;
  displayName: string | null;
  email: string | null;
  source: AccountSource;
  role: UserRole | null;
  roleFrom: RoleFrom | null;
  disabled: boolean;
  lockedOutUntil: string | null;
  twoFactorEnabled: boolean;
  hasPassword: boolean;
  mustChangePassword: boolean;
  externalProvider: string | null;
  createdUtc: string;
  lastSignInUtc: string | null;
}

export interface CreateUserRequest {
  userName: string;
  displayName: string;
  email: string | null;
  role: UserRole;
}

export interface CreatedUser {
  user: UserView;
  // Shown once; the account has to replace it at its first sign-in.
  password: string;
}

// A null field stays as it is; an empty text clears the name or the address.
export interface UpdateUserRequest {
  displayName?: string | null;
  email?: string | null;
  role?: UserRole | null;
}

export interface UsersRemoved {
  userIds: string[];
}

export const usersQuery = queryOptions({
  queryKey: ["users"],
  queryFn: () => apiGet<UserView[]>("/api/users"),
});

export function createUser(request: CreateUserRequest): Promise<CreatedUser> {
  return apiPost<CreatedUser>("/api/users", request);
}

export function updateUser(id: string, request: UpdateUserRequest): Promise<UserView> {
  return apiPatchJson<UserView>(`/api/users/${id}`, request);
}

export function setUserDisabled(id: string, disabled: boolean): Promise<UserView> {
  return apiPost<UserView>(`/api/users/${id}/${disabled ? "disable" : "enable"}`);
}

export function resetPassword(id: string): Promise<{ password: string }> {
  return apiPost<{ password: string }>(`/api/users/${id}/reset-password`);
}

export function resetTwoFactor(id: string): Promise<UserView> {
  return apiPost<UserView>(`/api/users/${id}/reset-two-factor`);
}

export function deleteUser(id: string): Promise<void> {
  return apiDelete(`/api/users/${id}`);
}

function byName(a: UserView, b: UserView): number {
  return a.userName.localeCompare(b.userName, undefined, { sensitivity: "base" });
}

export function upsertUser(queryClient: QueryClient, user: UserView): void {
  queryClient.setQueryData(usersQuery.queryKey, (list) => upsertById(list, user, byName));
}

// Changes some fields of a listed account, for an answer that carries only what changed, such as a new password.
export function patchUser(queryClient: QueryClient, id: string, patch: Partial<UserView>): void {
  queryClient.setQueryData(usersQuery.queryKey, (list) =>
    list?.map((user) => (user.id === id ? { ...user, ...patch } : user)),
  );
}

export function removeUsers(queryClient: QueryClient, userIds: readonly string[]): void {
  queryClient.setQueryData(usersQuery.queryKey, (list) => removeByIds(list, userIds));
}

// The directory as configuration sets it up, with each mapped group's name as the directory has it.
export interface DirectoryView {
  enabled: boolean;
  host: string | null;
  baseDn: string | null;
  groupRoleMap: { group: string; name: string | null; role: UserRole }[];
}

// The name is null when the directory has none for the group.
export interface DirectoryGroup {
  distinguishedName: string;
  name: string | null;
  description: string | null;
}

// What a sign-in of the user would give, found without their password. message is the server's English, which
// directoryCheckText says in the person's language.
export interface DirectoryCheck {
  found: boolean;
  distinguishedName: string | null;
  displayName: string | null;
  groups: string[];
  matches: { group: string; role: UserRole }[];
  role: UserRole | null;
  message: string;
  messageCode?: string | null;
  messageArgs?: ServerArguments | null;
}

export function directoryCheckText(check: DirectoryCheck): string {
  return serverText(check.messageCode, check.messageArgs, check.message);
}

export const directoryQuery = queryOptions({
  queryKey: ["directory"],
  queryFn: () => apiGet<DirectoryView>("/api/directory"),
});

export function findGroups(query: string): Promise<DirectoryGroup[]> {
  const search = new URLSearchParams({ query, limit: "20" });

  return apiGet<DirectoryGroup[]>(`/api/directory/groups?${search.toString()}`);
}

export function checkDirectoryUser(userName: string): Promise<DirectoryCheck> {
  return apiPost<DirectoryCheck>("/api/directory/check", { userName });
}
