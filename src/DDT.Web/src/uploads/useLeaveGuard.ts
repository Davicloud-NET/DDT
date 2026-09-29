// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useBlocker, type ShouldBlockFn } from "@tanstack/react-router";
import { useEffect } from "react";

export type LeaveGuard = ReturnType<typeof useLeaveGuard>;

// Signing out ends the session the upload needs, so it is never held up. Other navigation inside the app asks
// first.
const leavesWithoutSigningOut: ShouldBlockFn = ({ next }) => next.routeId !== "/sign-in";

// Holds up leaving the page while a file uploads, since unmounting the upload panel stops the upload.
export function useLeaveGuard(uploading: boolean) {
  // The beforeunload listener below asks about reloads and closing the tab.
  const leaving = useBlocker({
    shouldBlockFn: leavesWithoutSigningOut,
    enableBeforeUnload: false,
    disabled: !uploading,
    withResolver: true,
  });

  // If the upload ends while the question is open, there's nothing left to lose. Staying on the page shows
  // its result.
  useEffect(() => {
    if (!uploading && leaving.status === "blocked") {
      leaving.reset();
    }
  }, [uploading, leaving]);

  useEffect(() => {
    if (!uploading) {
      return;
    }

    const warn = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };

    window.addEventListener("beforeunload", warn);

    return () => {
      window.removeEventListener("beforeunload", warn);
    };
  }, [uploading]);

  return leaving;
}
