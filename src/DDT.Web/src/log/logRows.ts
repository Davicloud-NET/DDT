// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Rows never wrap, so every row is this tall. LogRow's h-5 sets it.
export const ROW_HEIGHT = 20;

// Rows rendered above and below the visible ones, so fast scrolling shows no gap.
const OVERSCAN = 30;

// Without a layout, as in tests, the newest rows are rendered.
const UNMEASURED_ROWS = 100;

// Scrolling up further than this from the bottom pauses following.
const FOLLOW_SLACK = 2 * ROW_HEIGHT;

export interface LogScroll {
  total: number;
  // The viewport's height in pixels, 0 before it is measured.
  height: number;
  scrollTop: number;
  following: boolean;
}

// The rows to render, from start up to end: those in view and the overscan, or the newest while following.
export function rowsInView({ total, height, scrollTop, following }: LogScroll) {
  const visible = Math.ceil(height / ROW_HEIGHT);
  const start =
    height === 0
      ? Math.max(0, total - UNMEASURED_ROWS)
      : following
        ? Math.max(0, total - visible - OVERSCAN)
        : Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const end = height === 0 || following ? total : Math.min(total, start + visible + 2 * OVERSCAN);

  return { start, end };
}

// Scrolling up past the slack pauses following, and reaching the bottom again resumes it.
export function followingAfterScroll(following: boolean, fromBottom: number): boolean {
  if (following && fromBottom > FOLLOW_SLACK) {
    return false;
  }

  return !following && fromBottom < 1 ? true : following;
}
