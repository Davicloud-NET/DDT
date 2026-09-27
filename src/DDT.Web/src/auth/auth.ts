// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { ApiError, apiFetch, apiPost } from "@/lib/api";

// NoRole: the password was right, but none of the directory groups of the account maps to a role in DDT.
export type LoginStatus = "Succeeded" | "RequiresTwoFactor" | "LockedOut" | "Failed" | "NoRole";

export interface CurrentUser {
  id: string;
  userName: string;
  displayName: string | null;
  source: string;
  twoFactorEnabled: boolean;
  roles: string[];
  // Signed in with a password an administrator was shown: nothing but the account page answers until it is replaced.
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
