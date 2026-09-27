// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";

import type { ApiTokenView } from "./tokens";

// A revoked token stays revoked after it would have expired, so revoked comes first.
export type TokenState = "active" | "expired" | "revoked";

export function tokenState(token: ApiTokenView, now: number): TokenState {
  if (token.revokedUtc !== null) {
    return "revoked";
  }

  return Date.parse(token.expiresUtc) > now ? "active" : "expired";
}

export type TokenFilter = TokenState | "all";

export const tokenFilters: { id: TokenFilter; label: MessageDescriptor }[] = [
  { id: "active", label: msg`Active` },
  { id: "expired", label: msg`Expired` },
  { id: "revoked", label: msg`Revoked` },
  { id: "all", label: msg`All` },
];

export function inTokenFilter(token: ApiTokenView, filter: TokenFilter, now: number): boolean {
  return filter === "all" || tokenState(token, now) === filter;
}

export function matchesToken(token: ApiTokenView, needle: string): boolean {
  return [token.name, token.userName, token.hint].some((value) =>
    value.toLowerCase().includes(needle),
  );
}

// Tokens that still work first, each group newest first as the server sends them.
export function byState(tokens: readonly ApiTokenView[], now: number): ApiTokenView[] {
  const rank: Record<TokenState, number> = { active: 0, expired: 1, revoked: 2 };

  return [...tokens].sort((a, b) => rank[tokenState(a, now)] - rank[tokenState(b, now)]);
}

// The token as a revoke leaves it, until the hub brings the server's copy with the same change.
export function revokedCopy(token: ApiTokenView, by: string, now: number): ApiTokenView {
  return { ...token, revokedUtc: new Date(now).toISOString(), revokedByName: by };
}

export const TOKEN_DAYS = { min: 1, max: 365, default: 90 };
