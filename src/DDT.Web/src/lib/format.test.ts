// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { formatBytes, formatDuration, percentOf } from "./format";

describe("formatBytes", () => {
  it("uses binary multiples with the units Windows shows", () => {
    expect(formatBytes(0)).toBe("0 bytes");
    expect(formatBytes(512)).toBe("512 bytes");
    expect(formatBytes(1024)).toBe("1 KB");
    expect(formatBytes(8 * 1024 * 1024)).toBe("8 MB");
    expect(formatBytes(64 * 1024 ** 3)).toBe("64 GB");
    expect(formatBytes(2 * 1024 ** 4)).toBe("2 TB");
  });
});

describe("formatDuration", () => {
  it("names seconds, minutes and hours", () => {
    expect(formatDuration(0)).toBe("0 s");
    expect(formatDuration(59_999)).toBe("59 s");
    expect(formatDuration(125_000)).toBe("2 min 5 s");
    expect(formatDuration(3_600_000 + 12 * 60_000 + 30_000)).toBe("1 h 12 min");
  });

  it("shows no negative time when the clocks of browser and server differ", () => {
    expect(formatDuration(-5_000)).toBe("0 s");
  });
});

describe("percentOf", () => {
  it("rounds down, so 100% means everything is there", () => {
    expect(percentOf(999, 1000)).toBe(99);
    expect(percentOf(1000, 1000)).toBe(100);
    expect(percentOf(6_262_478_328, 6_262_478_328)).toBe(100);
    expect(percentOf(0, 0)).toBe(0);
  });
});
