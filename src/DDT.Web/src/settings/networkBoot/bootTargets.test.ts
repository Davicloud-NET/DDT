// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// @vitest-environment node

import { describe, expect, it } from "vitest";

import type { BootTargetSettings, PxeHostInterfaces } from "../networkBoot";

import {
  architecturesWithout,
  bootFileOptions,
  newBootTarget,
  serverNameOptions,
  targetKeysInOrder,
} from "./bootTargets";

const target: BootTargetSettings = {
  method: "Tftp",
  bootFile: null,
  serverAddress: null,
  serverHostName: null,
  advertiseBootServerDiscovery: false,
};

describe("the boot targets", () => {
  it("start x64 targets on the 2011 boot manager, as a URL on the boot port for HTTP", () => {
    expect(newBootTarget("X64Uefi", "ddt.example", 8080)).toEqual({
      ...target,
      bootFile: "x64/bootmgfw.efi",
    });
    expect(newBootTarget("X64UefiHttp", "ddt.example", 8080)).toEqual({
      ...target,
      method: "Http",
      bootFile: "http://ddt.example:8080/boot/x64/bootmgfw.efi",
    });
  });

  it("leave the boot file empty without an x64 boot manager or a known boot port", () => {
    expect(newBootTarget("Arm64Uefi", "ddt.example", 8080).bootFile).toBeNull();
    expect(newBootTarget("X64UefiHttp", "ddt.example", null)).toEqual({
      ...target,
      method: "Http",
    });
  });

  it("list the described architectures first and keys that are no architecture last", () => {
    const keys = targetKeysInOrder({ Custom: target, x86bios: target, X64Uefi: target });

    expect(keys).toEqual(["X64Uefi", "x86bios", "Custom"]);
    expect(architecturesWithout(["x64uefi"])).not.toContain("X64Uefi");
  });

  it("offer boot managers as URLs only while the boot port is known", () => {
    expect(bootFileOptions(false, "ddt", null).map((option) => option.file)).toEqual([
      "x64/bootmgfw.efi",
      "x64/bootmgfw_ex.efi",
    ]);
    expect(bootFileOptions(true, "ddt", null)).toEqual([]);
    expect(bootFileOptions(true, "ddt", 80)[1]).toEqual({
      file: "http://ddt:80/boot/x64/bootmgfw_ex.efi",
      authority: "2023",
    });
  });

  it("offer this host's name, then each served address once", () => {
    const hosts: PxeHostInterfaces[] = [
      {
        host: "ddt-01",
        updatedUtc: null,
        unmatched: [],
        interfaces: [
          { name: "Ethernet", addresses: ["10.0.0.5", "ddt"], served: true },
          { name: "Wi-Fi", addresses: ["192.168.1.2"], served: false },
        ],
      },
      {
        host: "ddt-02",
        updatedUtc: null,
        unmatched: [],
        interfaces: [{ name: "Ethernet", addresses: ["10.0.0.5"], served: true }],
      },
    ];

    expect(serverNameOptions("ddt", hosts)).toEqual(["ddt", "10.0.0.5"]);
  });
});
