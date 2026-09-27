// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

/// <reference types="node" />
// @vitest-environment node

import { readFileSync } from "node:fs";

import { describe, expect, it } from "vitest";

import {
  architecturesInOrder,
  canonicalArchitecture,
  clientArchitectures,
  methodFor,
  otherEntries,
  withEntries,
  withInterface,
  type PxeInterface,
} from "./networkBoot";

const ethernet: PxeInterface = { name: "Ethernet", addresses: ["192.168.1.10"], served: false };

describe("the network boot helpers", () => {
  it("offer the architectures the server knows, and each once", () => {
    const source = readFileSync(
      new URL("../../../DDT.Protocols/Dhcp/ClientArchitecture.cs", import.meta.url),
      "utf8",
    );
    const members = [...source.matchAll(/^\s+(\w+) = 0x[0-9A-F]+,/gm)].map((match) => match[1]);

    expect(members.length).toBeGreaterThan(40);
    expect([...clientArchitectures]).toEqual(members);
    expect([...architecturesInOrder].sort()).toEqual([...clientArchitectures].sort());
  });

  it("match a key to its architecture without regard to case, and pick the method from it", () => {
    expect(canonicalArchitecture(" x64uefi ")).toBe("X64Uefi");
    expect(canonicalArchitecture("7")).toBeNull();
    expect(methodFor("X64Uefi")).toBe("Tftp");
    expect(methodFor("Arm64UefiHttp")).toBe("Http");
  });

  it("list an interface by its name, and take out its name and addresses", () => {
    expect(withInterface([], ethernet, true)).toEqual(["Ethernet"]);
    expect(withInterface(["192.168.1.10"], ethernet, true)).toEqual(["192.168.1.10"]);
    expect(withInterface(["ETHERNET", "192.168.1.10", "eth9"], ethernet, false)).toEqual(["eth9"]);
  });

  it("add typed entries once, and tell them from the reported names", () => {
    expect(withEntries(["Ethernet"], " ethernet, 10.0.0.5,,eth9 ")).toEqual([
      "Ethernet",
      "10.0.0.5",
      "eth9",
    ]);
    expect(
      otherEntries(
        ["ethernet", "10.0.0.5"],
        [{ host: "ddt-01", updatedUtc: null, interfaces: [ethernet], unmatched: [] }],
      ),
    ).toEqual(["10.0.0.5"]);
  });
});
