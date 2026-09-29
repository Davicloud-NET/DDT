// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { UNSTABLE_ToastRegion as AriaToastRegion } from "react-aria-components";

import { Toast } from "./Toast";
import { toasts } from "./toasts";

// Where the toasts of every page show, in the bottom right corner.
export function ToastRegion() {
  return (
    <AriaToastRegion
      queue={toasts}
      className="fixed right-4 bottom-4 z-50 flex w-90 max-w-[calc(100vw-2rem)] flex-col gap-2 outline-none"
    >
      {({ toast }) => <Toast toast={toast} />}
    </AriaToastRegion>
  );
}
