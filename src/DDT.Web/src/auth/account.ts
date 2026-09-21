// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { apiPost } from "@/lib/api";

export interface TwoFactorEnrollment {
  sharedKey: string;
  authenticatorUri: string;
}

export interface RecoveryCodes {
  codes: string[];
}

export function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  return apiPost("/api/auth/password", { currentPassword, newPassword });
}

export function startTwoFactorEnrollment(): Promise<TwoFactorEnrollment> {
  return apiPost<TwoFactorEnrollment>("/api/auth/2fa/enroll");
}

export function enableTwoFactor(code: string): Promise<RecoveryCodes> {
  return apiPost<RecoveryCodes>("/api/auth/2fa/enable", { code });
}

export function disableTwoFactor(code: string): Promise<void> {
  return apiPost("/api/auth/2fa/disable", { code });
}

export function regenerateRecoveryCodes(): Promise<RecoveryCodes> {
  return apiPost<RecoveryCodes>("/api/auth/2fa/recovery-codes");
}
