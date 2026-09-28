// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useDebouncedValue } from "./useDebouncedValue";

describe("useDebouncedValue", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function renderDebounced(initial: string) {
    return renderHook(({ value }) => useDebouncedValue(value, 300), {
      initialProps: { value: initial },
    });
  }

  it("starts with the first value", () => {
    const { result } = renderDebounced("lab");

    expect(result.current).toBe("lab");
  });

  it("takes a new value once it has rested for the delay", () => {
    const { result, rerender } = renderDebounced("");

    rerender({ value: "lab" });
    act(() => {
      vi.advanceTimersByTime(299);
    });
    expect(result.current).toBe("");

    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(result.current).toBe("lab");
  });

  it("waits again after every change, so typing only settles on the last value", () => {
    const { result, rerender } = renderDebounced("");

    rerender({ value: "l" });
    act(() => {
      vi.advanceTimersByTime(200);
    });
    rerender({ value: "la" });
    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(result.current).toBe("");

    rerender({ value: "lab" });
    act(() => {
      vi.advanceTimersByTime(300);
    });
    expect(result.current).toBe("lab");
  });
});
