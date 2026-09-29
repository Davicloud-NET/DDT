// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient } from "@tanstack/react-query";
import { act, renderHook } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { describe, expect, it } from "vitest";

import { testHub } from "@/test/fakeHub";
import { settle } from "@/test/settle";

import { LiveContext } from "./LiveContext";
import { useMachineWatch } from "./useMachineWatch";

describe("useMachineWatch", () => {
  it("is offline without a connection", () => {
    const { result } = renderHook(() => useMachineWatch("m1", {}));

    expect(result.current).toBe("offline");
  });

  it("watches the machine while mounted, hands over its events and follows the status", async () => {
    const { live, invocations, push } = testHub(new QueryClient());
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(LiveContext, { value: live }, children);
    const received: unknown[] = [];

    const { result, unmount } = renderHook(
      () =>
        useMachineWatch("m1", {
          onLogAppended: (event) => received.push(event),
        }),
      { wrapper },
    );

    expect(result.current).toBe("offline");

    await act(async () => {
      live.start();
      await settle();
    });

    expect(result.current).toBe("live");
    expect(invocations).toEqual(["WatchMachine m1"]);

    act(() => {
      push("machineLogAppended", { machineId: "m1", lastLineId: 3 });
    });

    expect(received).toEqual([{ machineId: "m1", lastLineId: 3 }]);

    unmount();

    expect(invocations).toEqual(["WatchMachine m1", "UnwatchMachine m1"]);
  });
});
