// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { backoff } from "./backoff";

describe("backoff", () => {
  it.each([
    [0, 1_000],
    [1, 1_000],
    [2, 2_000],
    [3, 4_000],
    [5, 16_000],
    [6, 30_000],
    [20, 30_000],
  ])("waits after %i failures %i ms", (failures, wait) => {
    expect(backoff(failures)).toBe(wait);
  });
});
