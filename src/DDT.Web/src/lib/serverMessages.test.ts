// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

/// <reference types="node" />
// @vitest-environment node

import { readFileSync } from "node:fs";

import { describe, expect, it } from "vitest";

import {
  readServerMessages,
  renderWebCatalog,
  webCatalogPath,
} from "../../scripts/server-messages.mjs";

describe("the server's messages", () => {
  // The server's test TheWebCatalogIsCurrent keeps server-messages.json the server's catalog.
  it("are what serverMessages.ts was written from", () => {
    const written = readFileSync(webCatalogPath, "utf8").replace(/\r\n/g, "\n");

    expect(written, "scripts/server-messages.json changed; run npm run messages").toBe(
      renderWebCatalog(readServerMessages()),
    );
  });

  it("each have a stable code and an English text", () => {
    const messages = readServerMessages();

    expect(Object.keys(messages).length).toBeGreaterThan(0);

    for (const [code, english] of Object.entries(messages)) {
      expect(code).toMatch(/^[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)+$/);
      expect(english).not.toBe("");
    }
  });
});
