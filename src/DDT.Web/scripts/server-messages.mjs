// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Turns server-messages.json, the server's catalog of message codes and their English, into src/lib/serverMessages.ts:
// one Lingui message per code, whose English `npm run i18n` extracts for translation. The server test
// TheWebCatalogIsCurrent writes the JSON from src/DDT.Contracts/Messages/ServerMessages.cs, and a web test fails while
// the written file differs from what this renders. Run it from src/DDT.Web with `npm run messages`.

import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const here = (path) => fileURLToPath(new URL(path, import.meta.url));

export const serverMessagesPath = here("./server-messages.json");
export const webCatalogPath = here("../src/lib/serverMessages.ts");

const header = [
  "// Copyright (C) 2026 Davicloud",
  "// SPDX-License-Identifier: GPL-3.0-or-later",
  "// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.",
  "",
  "// Written by scripts/server-messages.mjs from scripts/server-messages.json, which the server writes from its",
  "// catalog. Change src/DDT.Contracts/Messages/ServerMessages.cs, run the server's tests, then npm run messages.",
];

export function readServerMessages() {
  return JSON.parse(readFileSync(serverMessagesPath, "utf8"));
}

// The code is the message's context, so each code has an entry of its own in the catalog, by which a translator
// sees where the server says it, even where the web says the same English elsewhere. JSON strings are valid
// JavaScript strings, and Lingui's macro reads a string's value, so the English arrives exactly as the server has it.
export function renderWebCatalog(messages) {
  const entries = Object.keys(messages)
    .sort()
    .map((code) =>
      [
        `  ${JSON.stringify(code)}: msg({`,
        `    context: ${JSON.stringify(code)},`,
        `    message: ${JSON.stringify(messages[code])},`,
        "  }),",
      ].join("\n"),
    );

  return [
    ...header,
    "",
    'import type { MessageDescriptor } from "@lingui/core";',
    'import { msg } from "@lingui/core/macro";',
    "",
    "export const serverMessages: Readonly<Record<string, MessageDescriptor>> = {",
    ...entries,
    "};",
    "",
  ].join("\n");
}

function main() {
  writeFileSync(webCatalogPath, renderWebCatalog(readServerMessages()), "utf8");
  console.log(`Wrote ${webCatalogPath}`);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main();
}
