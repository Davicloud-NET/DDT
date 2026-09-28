// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { ApiError, apiFetch, apiGet, apiPost } from "@/lib/api";

// NoRole: the password was right, but none of the account's directory groups maps to a role in DDT.
export type LoginStatus = "Succeeded" | "RequiresTwoFactor" | "LockedOut" | "Failed" | "NoRole";

export interface CurrentUser {
  id: string;
  userName: string;
  displayName: string | null;
  source: string;
  twoFactorEnabled: boolean;
  roles: string[];
  // The user signed in with a password an administrator was shown. Only the account page works until they replace it.
  mustChangePassword: boolean;
}

export interface LoginRequest {
  userName: string;
  password: string;
  twoFactorCode?: string;
  recoveryCode?: string;
}

export const currentUserQuery = queryOptions({
  queryKey: ["current-user"],
  staleTime: 30_000,
  retry: false,
  queryFn: async (): Promise<CurrentUser | null> => {
    const response = await apiFetch("/api/auth/me");

    if (response.status === 401) {
      return null;
    }

    if (!response.ok) {
      throw new ApiError(response.status, response.statusText);
    }

    return (await response.json()) as CurrentUser;
  },
});

// A single sign-on provider the sign-in page offers a button for. The list is empty while single sign-on is off.
export interface ExternalProvider {
  scheme: string;
  displayName: string;
}

export const externalProvidersQuery = queryOptions({
  queryKey: ["external-providers"],
  staleTime: Infinity,
  retry: false,
  queryFn: () => apiGet<ExternalProvider[]>("/api/auth/external/providers"),
});

// Where a provider's button goes. The server sends the browser on to the provider. It comes back to /, or to the
// sign-in page with the reason for the refusal.
export function externalSignInUrl(provider: ExternalProvider): string {
  return `/api/auth/external/start?${new URLSearchParams({ scheme: provider.scheme }).toString()}`;
}

export async function login(request: LoginRequest): Promise<LoginStatus> {
  try {
    const result = await apiPost<{ status: LoginStatus }>("/api/auth/login", request);

    return result.status;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return "Failed";
    }

    throw error;
  }
}

export async function logout(queryClient: QueryClient): Promise<void> {
  await apiPost("/api/auth/logout");
  queryClient.setQueryData(currentUserQuery.queryKey, null);
  await queryClient.invalidateQueries();
}
