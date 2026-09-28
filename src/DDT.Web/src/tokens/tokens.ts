// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost } from "@/lib/api";
import { upsertById } from "@/lib/listCache";

export type TokenRole = "Administrator" | "Operator" | "Viewer";

// An API token as the server shows it: never its secret, only its last four characters as hint. A revoked token keeps
// its row, with who revoked it.
export interface ApiTokenView {
  id: string;
  name: string;
  role: TokenRole;
  userId: string;
  userName: string;
  hint: string;
  createdUtc: string;
  expiresUtc: string;
  lastUsedUtc: string | null;
  lastUsedAddress: string | null;
  revokedUtc: string | null;
  revokedByName: string | null;
}

export interface CreatedApiToken {
  token: ApiTokenView;
  // Shown once: the server keeps only its hash.
  secret: string;
}

export interface CreateApiTokenRequest {
  name: string;
  role: TokenRole;
  expiresInDays: number;
}

// The signed-in person's own tokens.
export const ownTokensQuery = queryOptions({
  queryKey: ["tokens", "own"],
  queryFn: () => apiGet<ApiTokenView[]>("/api/tokens"),
});

// Every user's tokens, for administrators.
export const allTokensQuery = queryOptions({
  queryKey: ["tokens", "all"],
  queryFn: () => apiGet<ApiTokenView[]>("/api/tokens/all"),
});

export function createToken(request: CreateApiTokenRequest): Promise<CreatedApiToken> {
  return apiPost<CreatedApiToken>("/api/tokens", request);
}

export function revokeToken(id: string): Promise<void> {
  return apiDelete(`/api/tokens/${id}`);
}

export function isActiveToken(token: ApiTokenView, now: number): boolean {
  return token.revokedUtc === null && Date.parse(token.expiresUtc) > now;
}

function newestFirst(a: ApiTokenView, b: ApiTokenView): number {
  return Date.parse(b.createdUtc) - Date.parse(a.createdUtc);
}

// A token created, used or revoked, from an answer or from the hub's tokenChanged, which may arrive twice.
export function upsertToken(
  queryClient: QueryClient,
  token: ApiTokenView,
  ownUserId: string | null,
): void {
  const upsert = (list: ApiTokenView[] | undefined) => upsertById(list, token, newestFirst);

  queryClient.setQueryData(allTokensQuery.queryKey, upsert);

  if (token.userId === ownUserId) {
    queryClient.setQueryData(ownTokensQuery.queryKey, upsert);
  }
}

// A deleted user's tokens are deleted with it.
export function removeTokensOf(queryClient: QueryClient, userIds: readonly string[]): void {
  const removed = new Set(userIds);

  queryClient.setQueryData(allTokensQuery.queryKey, (list) =>
    list?.filter((token) => !removed.has(token.userId)),
  );
}
