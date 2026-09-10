import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { ApiError, apiFetch, apiPost } from "@/lib/api";

export type LoginStatus = "Succeeded" | "RequiresTwoFactor" | "LockedOut" | "Failed";

export interface CurrentUser {
  id: string;
  userName: string;
  displayName: string | null;
  source: string;
  twoFactorEnabled: boolean;
  roles: string[];
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
