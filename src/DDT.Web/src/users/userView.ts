// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { ApiError } from "@/lib/api";

import type { UserRole, UserView } from "./users";

// How the Users page names accounts, their roles and where those come from.

// Highest first, as a role includes the ones below it.
export const ROLES: readonly UserRole[] = ["Administrator", "Operator", "Viewer"];

export function roleLabel(role: string): string {
  switch (role) {
    case "Administrator":
      return t`Administrator`;
    case "Operator":
      return t`Operator`;
    case "Viewer":
      return t`Viewer`;
    default:
      return role;
  }
}

export function roleDescription(role: UserRole): string {
  switch (role) {
    case "Administrator":
      return t`Changes everything, including accounts and settings.`;
    case "Operator":
      return t`Approves machines and starts deployments.`;
    case "Viewer":
      return t`Sees machines and runs, changes nothing.`;
  }
}

// The highest of the roles an account holds, or null for none.
export function highestRole(roles: readonly string[]): UserRole | null {
  return ROLES.find((role) => roles.includes(role)) ?? null;
}

// The roles at or below one, for a choice that may not go above it.
export function rolesUpTo(role: UserRole | null): UserRole[] {
  return role === null ? [] : ROLES.slice(ROLES.indexOf(role));
}

export function sourceLabel(source: string): string {
  switch (source) {
    case "Local":
      return t`Local account`;
    case "Directory":
      return t`Directory account`;
    case "External":
      return t`Single sign-on account`;
    default:
      return source;
  }
}

// The display name when the account has one, else the user name.
export function shownName(user: { userName: string; displayName: string | null }): string {
  const display = user.displayName?.trim();

  return display !== undefined && display !== "" ? display : user.userName;
}

// True when the account's groups decide its role at each sign-in, so the Users page cannot change it.
export function groupsDecideRole(user: UserView): boolean {
  return user.roleFrom === "DirectoryGroups" || user.roleFrom === "SingleSignOnGroups";
}

// Where the role in the list comes from, in a few words under it.
export function roleOrigin(user: UserView): string | null {
  switch (user.roleFrom) {
    case "Manual":
      return t`Set by an administrator`;
    case "DirectoryGroups":
      return t`From its directory groups`;
    case "SingleSignOnGroups":
      return t`From its single sign-on groups`;
    case "Provisioned":
      return t`Given when single sign-on created it`;
    case null:
      return user.role === null ? t`Reaches nothing` : null;
  }
}

export function matchesUser(user: UserView, needle: string): boolean {
  return [user.userName, user.displayName, user.email, user.externalProvider].some(
    (value) => value?.toLowerCase().includes(needle) === true,
  );
}

export function isLockedOut(user: UserView, now: number): boolean {
  return user.lockedOutUntil !== null && Date.parse(user.lockedOutUntil) > now;
}

// What deleting the account leaves behind, and what keeps it out instead.
export function userDeletionConsequence(user: UserView): string {
  const name = user.userName;

  switch (user.source) {
    case "Directory":
      return t`${name} and its API tokens are deleted from DDT. The audit log keeps its entries. If its directory groups still give it a role, it comes back at its next sign-in as a new account; to keep it out, disable it instead.`;
    case "External":
      return t`${name} and its API tokens are deleted from DDT. The audit log keeps its entries. If single sign-on creates accounts, it comes back at its next sign-in as a new account; to keep it out, disable it instead.`;
    case "Local":
      return t`${name} and its API tokens are deleted. The audit log keeps its entries. This cannot be undone; to keep the account out for now, disable it instead.`;
  }
}

// A validation refusal names its fields; these are the messages for one of them.
export function fieldErrors(error: Error | null, field: string): string[] {
  return error instanceof ApiError && error.status === 400
    ? (error.problem?.errors?.[field] ?? [])
    : [];
}

// The refusal to show below a form: anything that is not a message for one of its fields.
export function formError(error: Error | null, fields: readonly string[]): string | null {
  if (error === null) {
    return null;
  }

  const errors =
    error instanceof ApiError && error.status === 400 ? error.problem?.errors : undefined;

  if (errors === undefined || Object.keys(errors).length === 0) {
    return error.message;
  }

  const other = Object.entries(errors)
    .filter(([key]) => !fields.includes(key))
    .flatMap(([, messages]) => messages);

  return other.length > 0 ? other.join(" ") : null;
}
