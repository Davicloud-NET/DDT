// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import {
  Button as AriaButton,
  Text,
  UNSTABLE_Toast as AriaToast,
  UNSTABLE_ToastContent as ToastContent,
  UNSTABLE_ToastRegion as ToastRegion,
} from "react-aria-components";

import { cx } from "./cx";
import { toasts } from "./toasts";

const marks: Record<"ok" | "fail" | "info", string> = {
  ok: "bg-ok",
  fail: "bg-fail",
  info: "bg-run",
};

export function Toasts() {
  const { t } = useLingui();

  return (
    <ToastRegion
      queue={toasts}
      className="fixed right-4 bottom-4 z-50 flex w-90 max-w-[calc(100vw-2rem)] flex-col gap-2 outline-none"
    >
      {({ toast }) => (
        <AriaToast
          toast={toast}
          className="flex items-stretch overflow-hidden rounded-overlay bg-raised shadow-overlay outline-none entering:animate-toast-in focus-visible:outline-2 focus-visible:outline-focus"
        >
          <span
            aria-hidden="true"
            className={cx("w-1.5 shrink-0", marks[toast.content.tone ?? "info"])}
          />
          <ToastContent className="flex min-w-0 flex-1 flex-col gap-0.5 px-3.5 py-3">
            <Text slot="title" className="type-label text-ink">
              {toast.content.title}
            </Text>
            {toast.content.description ? (
              <Text slot="description" className="type-small text-ink-2">
                {toast.content.description}
              </Text>
            ) : null}
          </ToastContent>
          <AriaButton
            slot="close"
            aria-label={t`Close`}
            className="flex w-9 shrink-0 cursor-pointer items-start justify-center pt-3 text-muted outline-none hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
          >
            <IconX size={16} stroke={2} />
          </AriaButton>
        </AriaToast>
      )}
    </ToastRegion>
  );
}
