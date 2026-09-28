// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { afterEach, describe, expect, it, vi } from "vitest";

import { resumableUpload } from "./resumableUpload";

const sessionId = "0193a4b2-0000-7000-8000-0000000000u3";

// The protocol itself is tested through uploadImage in images/upload.test.ts. This file covers what the kind changes.
function serve(completion: unknown) {
  const bodies: unknown[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const path = input instanceof Request ? input.url : input.toString();
      const method = init?.method ?? "GET";

      if (path === "/api/auth/session") {
        return Promise.resolve(new Response(null, { headers: { "X-CSRF-TOKEN": "token" } }));
      }

      if (method === "POST" && path === "/api/images/uploads") {
        bodies.push(JSON.parse(typeof init?.body === "string" ? init.body : "null"));

        return Promise.resolve(
          new Response(
            JSON.stringify({
              id: sessionId,
              fileName: "drivers.zip",
              length: 4,
              lastModified: 1_000,
              offset: 0,
              chunkBytes: 8,
              kind: "Drivers",
            }),
            { status: 201 },
          ),
        );
      }

      if (method === "PATCH") {
        return Promise.resolve(
          new Response(null, { status: 204, headers: { "Upload-Offset": "4" } }),
        );
      }

      return Promise.resolve(new Response(JSON.stringify(completion), { status: 201 }));
    }),
  );

  return bodies;
}

describe("resumableUpload", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("names the kind of a package and hands back what the library holds now", async () => {
    const bodies = serve({ id: "p1", name: "Latitude 7440 drivers" });

    const result = await resumableUpload<{ id: string; name: string }>(
      new File(["zip!"], "drivers.zip", { lastModified: 1_000 }),
      "Drivers",
      { signal: new AbortController().signal },
    );

    expect(bodies).toEqual([
      { fileName: "drivers.zip", length: 4, lastModified: 1_000, kind: "Drivers" },
    ]);
    expect(result).toEqual({
      outcome: "added",
      library: { id: "p1", name: "Latitude 7440 drivers" },
    });
  });
});
