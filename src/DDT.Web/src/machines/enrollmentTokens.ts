import { queryOptions } from "@tanstack/react-query";

import { apiGet, apiPost } from "@/lib/api";

export interface EnrollmentTokenSummary {
  id: string;
  name: string;
  createdUtc: string;
  expiresUtc: string;
  revokedUtc: string | null;
}

export interface CreatedEnrollmentToken {
  summary: EnrollmentTokenSummary;
  token: string;
}

export const enrollmentTokensQuery = queryOptions({
  queryKey: ["enrollment-tokens"],
  queryFn: () => apiGet<EnrollmentTokenSummary[]>("/api/enrollment-tokens"),
});

export function createEnrollmentToken(
  name: string,
  validForDays: number,
): Promise<CreatedEnrollmentToken> {
  return apiPost<CreatedEnrollmentToken>("/api/enrollment-tokens", { name, validForDays });
}

export function revokeEnrollmentToken(id: string): Promise<EnrollmentTokenSummary> {
  return apiPost<EnrollmentTokenSummary>(`/api/enrollment-tokens/${id}/revoke`);
}
