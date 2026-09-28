// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { useLayoutEffect, useRef, useSyncExternalStore } from "react";
import {
  Button as AriaButton,
  Text,
  UNSTABLE_Toast as AriaToast,
  UNSTABLE_ToastContent as ToastContent,
  type QueuedToast,
} from "react-aria-components";

import { buttonClass } from "./buttonClass";
import { cx } from "./cx";
import { toasts, type ToastMessage } from "./toasts";

const marks: Record<"ok" | "fail" | "info", string> = {
  ok: "bg-ok",
  fail: "bg-fail",
  info: "bg-run",
};

// A toast rises into place when it comes, and sinks back as it leaves, faster.
export function Toast({ toast }: { toast: QueuedToast<ToastMessage> }) {
  const { t } = useLingui();
  const ref = useRef<HTMLDivElement>(null);
  const key = toast.key;
  const leaving = useSyncExternalStore(toasts.subscribeLeaving, () => toasts.isLeaving(key));

  // The close finishes once the exit animation has run, or at once if nothing runs: with "reduce motion", or in tests.
  useLayoutEffect(() => {
    if (!leaving) {
      return;
    }

    const element = ref.current;
    const running = element !== null && "getAnimations" in element ? element.getAnimations() : [];
    let current = true;

    if (running.length === 0) {
      toasts.finishClose(key);
      return;
    }

    void Promise.allSettled(running.map((animation) => animation.finished)).then(() => {
      if (current) {
        toasts.finishClose(key);
      }
    });

    return () => {
      current = false;
    };
  }, [leaving, key]);

  return (
    <AriaToast
      ref={ref}
      toast={toast}
      className={cx(
        "flex items-stretch overflow-hidden rounded-overlay bg-raised shadow-overlay outline-none focus-visible:outline-2 focus-visible:outline-focus",
        leaving ? "pointer-events-none animate-pop-out" : "animate-pop-in",
      )}
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
        {toast.content.action ? (
          <span className="pt-1.5">
            <AriaButton
              className={buttonClass("secondary", "sm")}
              onPress={() => {
                toast.content.action?.onAction();
                toasts.close(key);
              }}
            >
              {toast.content.action.label}
            </AriaButton>
          </span>
        ) : null}
      </ToastContent>
      <AriaButton
        slot="close"
        aria-label={t`Close`}
        className="flex w-9 shrink-0 cursor-pointer items-start justify-center pt-3 text-muted motion-colors outline-none hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
      >
        <IconX size={16} stroke={2} />
      </AriaButton>
    </AriaToast>
  );
}
