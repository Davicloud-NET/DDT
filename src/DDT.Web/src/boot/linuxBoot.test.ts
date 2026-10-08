// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { linuxBootView } from "@/test/builders";

import { keymapName, linuxBootProblem, linuxSourcesUrl, snapshotDate } from "./linuxBoot";

describe("keymapName", () => {
  it("names the layouts the environment brings, and leaves a name it does not know as it is", () => {
    expect(keymapName("us")).toBe("English (United States)");
    expect(keymapName("uk")).toBe("English (United Kingdom)");
    expect(keymapName("de")).toBe("German");
    expect(keymapName("ch")).toBe("German (Switzerland)");
    expect(keymapName("fr")).toBe("French");
    expect(keymapName("it")).toBe("Italian");
    expect(keymapName("es")).toBe("Spanish");
    expect(keymapName("dvorak")).toBe("dvorak");
  });
});

describe("snapshotDate", () => {
  it("reads the day from a snapshot time, whatever the browser's time zone", () => {
    expect(snapshotDate("20261006T203214Z", "en-GB")).toBe("6 October 2026");
    expect(snapshotDate("20261231T235959Z", "en-US")).toBe("December 31, 2026");
    expect(snapshotDate("20260101T000000Z", "de")).toBe("1. Januar 2026");
  });

  it("leaves a text that is no such time as it is", () => {
    expect(snapshotDate("yesterday", "en")).toBe("yesterday");
    expect(snapshotDate("", "en")).toBe("");
  });
});

describe("linuxBootProblem", () => {
  it("is nothing while machines can be started", () => {
    expect(linuxBootProblem(linuxBootView())).toBeNull();
  });

  it("says the server's sentence from its code, and its English for a code this build does not know", () => {
    const missing = linuxBootView({
      available: false,
      problem: "The server's own English.",
      problemText: { code: "linuxBoot.missing", args: { folder: "/app/linux-boot" } },
    });

    expect(linuxBootProblem(missing)).toBe(
      "This server has no Linux boot environment: /app/linux-boot holds no ddt-linux-boot.json. A release of DDT brings the folder with it.",
    );
    expect(
      linuxBootProblem({ ...missing, problemText: { code: "linuxBoot.fromLater", args: {} } }),
    ).toBe("The server's own English.");
    expect(linuxBootProblem({ ...missing, problemText: null })).toBe("The server's own English.");
  });

  it("says which file of a damaged environment is wrong", () => {
    const damaged = linuxBootView({
      available: false,
      problem: "English",
      problemText: {
        code: "linuxBoot.damaged",
        args: { folder: "/app/linux-boot", reason: "hash", name: "vmlinuz" },
      },
    });

    expect(linuxBootProblem(damaged)).toBe(
      "The Linux boot environment in /app/linux-boot is damaged: vmlinuz is not the file its description names. Install this release of DDT again, which brings the folder anew.",
    );
  });
});

describe("linuxSourcesUrl", () => {
  it("is the list of source packages among the server's legal texts", () => {
    expect(linuxSourcesUrl).toBe("/api/about/legal/licenses/linux-boot/SOURCES.md");
  });
});
