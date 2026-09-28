// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { createWatchRegistry, watchEventHandlers, type MachineLogAppended } from "./machineWatches";

describe("createWatchRegistry", () => {
  it("asks for a watch with the first watcher and an unwatch once the last one left", () => {
    const registry = createWatchRegistry();
    const first = { handlers: {} };
    const second = { handlers: {} };

    expect(registry.add("m1", first)).toBe(true);
    expect(registry.add("m1", second)).toBe(false);
    expect(registry.remove("m1", first)).toBe(false);
    expect(registry.remove("m1", first)).toBe(false);
    expect(registry.remove("m1", second)).toBe(true);
    expect(registry.machineIds()).toEqual([]);
    expect(registry.add("m1", first)).toBe(true);
  });

  it("ignores a repeated remove once the machine is watched again", () => {
    const registry = createWatchRegistry();
    const left = { handlers: {} };
    const staying = { handlers: {} };

    registry.add("m1", left);
    registry.remove("m1", left);
    registry.add("m1", staying);

    expect(registry.remove("m1", left)).toBe(false);
    expect(registry.has("m1", staying)).toBe(true);
    expect(registry.entries()).toEqual([{ machineId: "m1", watch: staying }]);
  });
});

describe("watchEventHandlers", () => {
  it("hands the events of a machine only to its watchers", () => {
    const registry = createWatchRegistry();
    const handlers = watchEventHandlers(registry);
    const first: unknown[] = [];
    const second: unknown[] = [];
    const watch = { handlers: { onLogAppended: (event: MachineLogAppended) => first.push(event) } };

    registry.add("m1", watch);
    registry.add("m2", { handlers: { onLogAppended: (event) => second.push(event) } });

    const lines: MachineLogAppended = { machineId: "m1", lastLineId: 7 };
    handlers.machineLogAppended(lines);

    expect(first).toEqual([lines]);
    expect(second).toEqual([]);

    registry.remove("m1", watch);
    handlers.machineLogAppended({ machineId: "m1", lastLineId: 8 });

    expect(first).toEqual([lines]);
  });
});
