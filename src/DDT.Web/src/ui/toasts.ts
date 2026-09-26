// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import { UNSTABLE_ToastQueue as ToastQueue } from "react-aria-components";

// Short-lived news about something the person did not do on this page, such as a machine finishing its run.
// Anything that needs an answer is a dialog or a notice instead. Failures stay until they are closed.
export interface ToastMessage {
  title: ReactNode;
  description?: ReactNode;
  tone?: "ok" | "fail" | "info";
}

export const toasts = new ToastQueue<ToastMessage>({ maxVisibleToasts: 4 });

export function showToast(message: ToastMessage): void {
  toasts.add(message, message.tone === "fail" ? {} : { timeout: 6000 });
}
