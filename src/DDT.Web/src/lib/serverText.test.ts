// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { afterEach, describe, expect, it } from "vitest";

import { isServerMessage, serverText } from "./serverText";

async function inGerman() {
  const { messages } = await import("../locales/de/messages.po");

  i18n.loadAndActivate({ locale: "de", messages });
}

describe("serverText", () => {
  afterEach(() => {
    i18n.loadAndActivate({ locale: "en", messages: {} });
  });

  it("says a code with its values in English", () => {
    expect(serverText("common.nameLength", { max: 128 }, "fallback")).toBe(
      "The name must have 1 to 128 characters and no control characters.",
    );
  });

  it("says a code in the person's language", async () => {
    await inGerman();

    expect(serverText("common.nameLength", { max: 128 }, "fallback")).toBe(
      "Der Name muss 1 bis 128 Zeichen und keine Steuerzeichen haben.",
    );
    expect(
      serverText(
        "sequence.chosenByRules",
        { count: 2 },
        "2 rules choose this sequence. Delete them or let them choose another sequence, then delete this one.",
      ),
    ).toBe(
      "2 Regeln wählen diese Tasksequenz. Löschen Sie sie oder lassen Sie sie eine andere Tasksequenz wählen, und löschen Sie dann diese.",
    );
  });

  it("says a message within a message in the same language", async () => {
    await inGerman();

    const rule = { code: "rule.forModelOfAnyMaker", args: { model: "Latitude 7*" } };

    expect(serverText("resolution.ruleChooses", { rule, sequence: "Lab" }, "fallback")).toBe(
      "Die Regel für das Modell Latitude 7* jedes Herstellers wählt Lab. Eine Regel wählt nur: Das Gerät braucht " +
        "weiterhin eine Freigabe im Web oder jemanden, der sich daran anmeldet, wo die Tasksequenz angeboten wird.",
    );
    expect(
      serverText(
        "machine.inState",
        { state: { code: "machineState.pending", args: {} } },
        "The machine is Pending.",
      ),
    ).toBe("Das Gerät hat den Zustand „Wartet“.");
  });

  // A server newer than this page may send a code the page doesn't know yet, even inside another message.
  it("keeps the server's English for a code it does not know", async () => {
    await inGerman();

    expect(serverText("machine.fromTheFuture", {}, "The machine is in the future.")).toBe(
      "The machine is in the future.",
    );
    expect(
      serverText(
        "deployment.rulesNoLongerChoose",
        { explanation: { code: "resolution.fromTheFuture", args: {} } },
        "The rules no longer choose that sequence for this machine.",
      ),
    ).toBe("The rules no longer choose that sequence for this machine.");
    expect(serverText(null, null, "No code at all.")).toBe("No code at all.");
  });

  it("tells a message from other values", () => {
    expect(isServerMessage({ code: "machine.inState", args: {} })).toBe(true);
    expect(isServerMessage({ code: "machine.inState" })).toBe(false);
    expect(isServerMessage("machine.inState")).toBe(false);
    expect(isServerMessage(null)).toBe(false);
  });
});
