// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { apiPost } from "@/lib/api";
import { equalJson } from "@/lib/equalJson";
import type { ServerMessage } from "@/lib/serverText";

import type { SecretAction } from "./settings";

// The sections ldap and oidc as the settings API has them, and their tests (docs/settings.md, section 6).

export type DirectoryTransport = "Ldaps" | "StartTls" | "UnencryptedDangerous";

// groupRoleMap maps a group's distinguished name to a role. timeout is a duration as .NET writes it, such as 00:00:10.
// A text field the page empties holds null until the server makes it empty text again.
export interface LdapSettings {
  enabled: boolean;
  host: string | null;
  port: number;
  transport: DirectoryTransport;
  baseDn: string | null;
  bindDn: string | null;
  userFilter: string | null;
  immutableIdAttribute: string | null;
  displayNameAttribute: string | null;
  emailAttribute: string | null;
  resolveNestedGroups: boolean;
  groupRoleMap: Record<string, string>;
  timeout: string;
}

// groupRoleMap maps a value of the groupsClaim claim to a role; while it has entries, autoProvisionRole is not used.
export interface OidcSettings {
  enabled: boolean;
  authority: string | null;
  clientId: string | null;
  displayName: string | null;
  scopes: string[];
  autoProvision: boolean;
  autoProvisionRole: string;
  groupsClaim: string | null;
  groupRoleMap: Record<string, string>;
}

// userFound and passwordAccepted are null when no user was named. role is the one the groups would give, null for
// none. proof is set when the one testing signed in with their own directory password and stayed an administrator.
export interface LdapTestResult {
  bound: boolean;
  userFound: boolean | null;
  passwordAccepted: boolean | null;
  groups: string[];
  role: string | null;
  message: string;
  // The message as a code with its values, said in the person's language.
  text?: ServerMessage | null;
  proof: string | null;
}

export interface OidcTestResult {
  reached: boolean;
  issuer: string | null;
  redirectUri: string;
  message: string;
  // The message as a code with its values, said in the person's language.
  text?: ServerMessage | null;
}

export interface LdapTestRequest {
  values: LdapSettings;
  secrets: Record<string, SecretAction>;
  userName: string | null;
  password: string | null;
}

// The values as the form holds them, before they are saved. A stored bind password goes only to the stored server.
export function testLdap(request: LdapTestRequest): Promise<LdapTestResult> {
  return apiPost<LdapTestResult>("/api/settings/ldap/test", request);
}

export function testOidc(authority: string): Promise<OidcTestResult> {
  return apiPost<OidcTestResult>("/api/settings/oidc/test", { authority });
}

// Where the provider sends a browser back after a single sign-on; it has to be registered there.
export function redirectUri(): string {
  return `${window.location.origin}/api/auth/external/callback`;
}

export const DIRECTORY_PROOF_HEADER = "X-DDT-Directory-Proof";

// The server accepts a proof for 5 minutes; the page stops sending it a little earlier.
export const DIRECTORY_PROOF_LIFETIME_MS = 5 * 60_000 - 10_000;

// A directory test's proof, with the values it was given for.
export interface DirectoryProof {
  token: string;
  values: LdapSettings;
  bindPassword: SecretAction;
  expires: number;
}

function text(value: string | null | undefined): string {
  return (value ?? "").trim();
}

// What decides whether and as whom a directory account signs in, as the server compares it for the proof: a
// directory administrator's save that changes any of it, or the bind password, needs a test of exactly these values.
function directoryPart(values: LdapSettings) {
  return {
    host: text(values.host),
    port: values.port,
    transport: values.transport,
    baseDn: text(values.baseDn),
    bindDn: text(values.bindDn),
    userFilter: text(values.userFilter),
    immutableIdAttribute: text(values.immutableIdAttribute),
    resolveNestedGroups: values.resolveNestedGroups,
    groupRoleMap: Object.fromEntries(
      Object.entries(values.groupRoleMap).map(([group, role]) => [
        group.trim().toUpperCase(),
        role.trim().toUpperCase(),
      ]),
    ),
  };
}

export function sameDirectory(a: LdapSettings, b: LdapSettings): boolean {
  return equalJson(directoryPart(a), directoryPart(b));
}

// The keys of a role map compare without regard to case on the server, so a key that differs only in case is the same.
export function hasEntry(map: Record<string, string>, key: string): boolean {
  return entryOf(map, key) !== undefined;
}

export function entryOf(map: Record<string, string>, key: string): string | undefined {
  const wanted = key.trim().toLowerCase();

  return Object.entries(map).find(([existing]) => existing.trim().toLowerCase() === wanted)?.[1];
}

function bindPasswordOf(secrets: Record<string, SecretAction>): SecretAction {
  return secrets.bindPassword ?? { action: "Keep" };
}

// Whether a save of these values needs a directory proof from the person saving.
export function needsDirectoryProof(
  signsInThroughDirectory: boolean,
  stored: LdapSettings,
  values: LdapSettings,
  secrets: Record<string, SecretAction>,
): boolean {
  return (
    signsInThroughDirectory &&
    values.enabled &&
    (!sameDirectory(stored, values) || bindPasswordOf(secrets).action !== "Keep")
  );
}

export function proofFits(
  proof: DirectoryProof,
  values: LdapSettings,
  secrets: Record<string, SecretAction>,
): boolean {
  return (
    sameDirectory(proof.values, values) && equalJson(proof.bindPassword, bindPasswordOf(secrets))
  );
}

// What the group search uses: it asks the directory as the section is saved.
const CONNECTION = ["enabled", "host", "port", "transport", "baseDn", "bindDn", "timeout"] as const;

export function connectionChanged(
  stored: LdapSettings,
  values: LdapSettings,
  secrets: Record<string, SecretAction>,
): boolean {
  return (
    CONNECTION.some((field) => {
      const before = stored[field];
      const after = values[field];

      return typeof before === "number" || typeof before === "boolean"
        ? before !== after
        : text(before) !== text(after as string | null);
    }) || bindPasswordOf(secrets).action !== "Keep"
  );
}

// A duration as .NET writes it, [-][d.]hh:mm:ss[.fffffff], in whole seconds; null for anything else.
export function secondsOf(span: string | null): number | null {
  const match = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(span ?? "");

  if (match === null) {
    return null;
  }

  const [, sign, days = "0", hours = "0", minutes = "0", seconds = "0", fraction = "0"] = match;
  const total =
    Number(days) * 86_400 +
    Number(hours) * 3_600 +
    Number(minutes) * 60 +
    Number(seconds) +
    Number(`0.${fraction}`);

  return Math.round(sign === undefined ? total : -total);
}

export function spanOf(totalSeconds: number): string {
  const whole = Math.max(0, Math.round(totalSeconds));
  const days = Math.floor(whole / 86_400);
  const pad = (value: number) => String(value).padStart(2, "0");
  const time = `${pad(Math.floor((whole % 86_400) / 3_600))}:${pad(Math.floor((whole % 3_600) / 60))}:${pad(whole % 60)}`;

  return days > 0 ? `${String(days)}.${time}` : time;
}
