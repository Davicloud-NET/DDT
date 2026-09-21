// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { afterEach, describe, expect, it, vi } from "vitest";

import { ApiError, apiPut } from "./api";

interface Sent {
  method: string;
  path: string;
  init: RequestInit | undefined;
}

// Answers every request except the CSRF token with the given response. The token is cached across tests,
// so every test hands out the same one.
function serve(answer: () => Response) {
  const sent: Sent[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const path = input instanceof Request ? input.url : input.toString();

      if (path === "/api/auth/session") {
        return Promise.resolve(new Response(null, { headers: { "X-CSRF-TOKEN": "token" } }));
      }

      sent.push({ method: init?.method ?? "GET", path, init });

      return Promise.resolve(answer());
    }),
  );

  return sent;
}

function json(body: unknown, status: number): Response {
  return new Response(JSON.stringify(body), { status });
}

async function refusal(promise: Promise<unknown>): Promise<ApiError> {
  const error = await promise.then(
    () => null,
    (reason: unknown) => reason,
  );

  if (!(error instanceof ApiError)) {
    throw new Error("The request was not refused with an ApiError.");
  }

  return error;
}

describe("apiPut", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("sends the body as JSON with the CSRF token and returns the answer", async () => {
    const sent = serve(() => json({ version: 4 }, 200));

    const saved = await apiPut<{ version: number }>("/api/task-sequences/1", {
      version: 3,
      name: "Install Windows",
    });

    expect(saved).toEqual({ version: 4 });
    expect(sent).toHaveLength(1);

    const [request] = sent;
    const headers = new Headers(request?.init?.headers);

    expect(request?.method).toBe("PUT");
    expect(request?.path).toBe("/api/task-sequences/1");
    expect(headers.get("Content-Type")).toBe("application/json");
    expect(headers.get("X-CSRF-TOKEN")).toBe("token");
    expect(request?.init?.body).toBe('{"version":3,"name":"Install Windows"}');
    expect(request?.init?.keepalive).toBe(false);
  });

  it("asks the browser to finish the request after the page is gone when told to", async () => {
    const sent = serve(() => new Response(null, { status: 204 }));

    await expect(apiPut("/api/task-sequences/1", {}, { keepalive: true })).resolves.toBeUndefined();

    expect(sent[0]?.init?.keepalive).toBe(true);
  });

  it("refuses with the status and the title when the version is stale", async () => {
    serve(() => json({ title: "admin saved this sequence meanwhile." }, 409));

    const error = await refusal(apiPut("/api/task-sequences/1", { version: 3 }));

    expect(error.status).toBe(409);
    expect(error.message).toBe("admin saved this sequence meanwhile.");
    expect(error.problem).toEqual({ title: "admin saved this sequence meanwhile." });
  });

  it("keeps the validation errors keyed by field", async () => {
    serve(() =>
      json(
        {
          title: "One or more validation errors occurred.",
          errors: { mac: ["A rule for this MAC already exists."], model: ["Enter a model."] },
        },
        400,
      ),
    );

    const error = await refusal(apiPut("/api/assignment-rules/1", { mac: "00155D010203" }));

    expect(error.status).toBe(400);
    expect(error.message).toBe("A rule for this MAC already exists.");
    expect(error.problem?.errors).toEqual({
      mac: ["A rule for this MAC already exists."],
      model: ["Enter a model."],
    });
  });

  it("falls back to the status when the refusal has no problem details", async () => {
    serve(() => new Response("Bad gateway", { status: 502 }));

    const error = await refusal(apiPut("/api/task-sequences/1", {}));

    expect(error.status).toBe(502);
    expect(error.message).toBe("The server answered with status 502.");
    expect(error.problem).toBeNull();
  });
});
