// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { afterEach, describe, expect, it, vi } from "vitest";

import type { ImageUploadSession } from "./images";
import { backoff, MAX_FAILURES, uploadImage, type UploadProgress } from "./upload";

const session: ImageUploadSession = {
  id: "0193a4b2-0000-7000-8000-0000000000u1",
  fileName: "boot.wim",
  length: 6,
  lastModified: 1_000,
  offset: 0,
  chunkBytes: 4,
};

const slicePath = `/api/images/uploads/${session.id}`;

type Answer = Response | Error | (() => Response | Promise<Response>);

// Answers each "METHOD path" from its queue in order; the last answer repeats.
function serve(queues: Record<string, Answer[]>) {
  const sent: { method: string; path: string; offset: string | null }[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const path = input instanceof Request ? input.url : input.toString();
      const method = init?.method ?? "GET";

      if (path === "/api/auth/session") {
        return Promise.resolve(new Response(null, { headers: { "X-CSRF-TOKEN": "token" } }));
      }

      sent.push({ method, path, offset: new Headers(init?.headers).get("Upload-Offset") });

      const queue = queues[`${method} ${path}`] ?? [];
      const answer = queue.length > 1 ? queue.shift() : queue[0];

      if (answer === undefined) {
        return Promise.resolve(new Response(null, { status: 404 }));
      }

      if (answer instanceof Error) {
        return Promise.reject(answer);
      }

      return Promise.resolve(answer instanceof Response ? answer.clone() : answer());
    }),
  );

  return sent;
}

function created(): Response {
  return new Response(JSON.stringify(session), { status: 201 });
}

function accepted(offset: number): Response {
  return new Response(null, { status: 204, headers: { "Upload-Offset": String(offset) } });
}

// The server's answer while another request holds the session: it names the offset it has stored.
function busy(offset: number): Response {
  return new Response(null, {
    status: 409,
    headers: { "Retry-After": "5", "Upload-Offset": String(offset) },
  });
}

function file(): File {
  return new File(["012345"], "boot.wim", { lastModified: 1_000 });
}

function recordingWait() {
  const waits: number[] = [];

  return {
    waits,
    wait: (milliseconds: number) => {
      waits.push(milliseconds);
      return Promise.resolve();
    },
  };
}

describe("uploadImage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("sends a slice again after a network error, waiting longer after each failure", async () => {
    const sent = serve({
      "POST /api/images/uploads": [created()],
      [`PATCH ${slicePath}`]: [
        new TypeError("Failed to fetch"),
        new Response(null, { status: 503 }),
        accepted(4),
        accepted(6),
      ],
      [`POST ${slicePath}/complete`]: [new Response("[]", { status: 201 })],
    });
    const { waits, wait } = recordingWait();
    const progress: UploadProgress[] = [];

    const result = await uploadImage(file(), {
      signal: new AbortController().signal,
      wait,
      onProgress: (update) => progress.push(update),
    });

    expect(result).toEqual({ outcome: "added", images: [] });
    expect(waits).toEqual([backoff(1), backoff(2)]);
    expect(waits).toEqual([1_000, 2_000]);
    expect(sent.filter((request) => request.method === "PATCH").map((r) => r.offset)).toEqual([
      "0",
      "0",
      "0",
      "4",
    ]);
    expect(progress.some((update) => update.retrying)).toBe(true);
    expect(progress.at(-1)).toEqual({
      phase: "verifying",
      offset: 6,
      length: 6,
      sentBytes: 6,
      retrying: false,
    });
  });

  it("waits as long as Retry-After asks", async () => {
    serve({
      "POST /api/images/uploads": [created()],
      [`PATCH ${slicePath}`]: [
        new Response(null, { status: 429, headers: { "Retry-After": "7" } }),
        accepted(4),
        busy(4),
        accepted(6),
      ],
      [`POST ${slicePath}/complete`]: [new Response("[]", { status: 200 })],
    });
    const { waits, wait } = recordingWait();

    const result = await uploadImage(file(), { signal: new AbortController().signal, wait });

    // The first complete request got the 200, so every image in the file was there before.
    expect(result.outcome).toBe("duplicate");
    expect(waits).toEqual([7_000, 5_000]);
  });

  it("waits when the server is busy with the upload, then sends the same slice again", async () => {
    const sent = serve({
      "POST /api/images/uploads": [created()],
      // A resend reaches the server while the lost request still holds the session.
      [`PATCH ${slicePath}`]: [busy(0), accepted(4), accepted(6)],
      [`POST ${slicePath}/complete`]: [new Response("[]", { status: 201 })],
    });
    const { waits, wait } = recordingWait();
    const progress: UploadProgress[] = [];

    const result = await uploadImage(file(), {
      signal: new AbortController().signal,
      wait,
      onProgress: (update) => progress.push(update),
    });

    expect(result.outcome).toBe("added");
    expect(waits).toEqual([5_000]);
    expect(sent.filter((request) => request.method === "PATCH").map((r) => r.offset)).toEqual([
      "0",
      "0",
      "4",
    ]);
    expect(progress.at(-1)).toMatchObject({ phase: "verifying", offset: 6, sentBytes: 6 });
  });

  it("does not call the images duplicates when an earlier complete may have added them", async () => {
    const sent = serve({
      "POST /api/images/uploads": [created()],
      [`PATCH ${slicePath}`]: [accepted(4), accepted(6)],
      // The first answer is lost at a proxy while the server works on; the second finds it still busy.
      [`POST ${slicePath}/complete`]: [
        new Response(null, { status: 504 }),
        new Response(null, { status: 409, headers: { "Retry-After": "5" } }),
        new Response("[]", { status: 200 }),
      ],
    });

    const result = await uploadImage(file(), {
      signal: new AbortController().signal,
      wait: () => Promise.resolve(),
    });

    expect(result.outcome).toBe("unclear");
    expect(sent.filter((request) => request.path.endsWith("/complete"))).toHaveLength(3);
  });

  it("goes back to sending slices when complete says the server holds fewer bytes", async () => {
    const sent = serve({
      "POST /api/images/uploads": [created()],
      [`PATCH ${slicePath}`]: [accepted(4), accepted(6), accepted(6)],
      [`POST ${slicePath}/complete`]: [
        new Response(null, { status: 409, headers: { "Upload-Offset": "4" } }),
        new Response("[]", { status: 201 }),
      ],
    });

    await uploadImage(file(), {
      signal: new AbortController().signal,
      wait: () => Promise.resolve(),
    });

    expect(sent.filter((request) => request.method === "PATCH").map((r) => r.offset)).toEqual([
      "0",
      "4",
      "4",
    ]);
  });

  it("gives up after too many failures in a row", async () => {
    serve({
      "POST /api/images/uploads": [created()],
      [`PATCH ${slicePath}`]: [new Response(null, { status: 500 })],
    });
    const { waits, wait } = recordingWait();

    await expect(
      uploadImage(file(), { signal: new AbortController().signal, wait }),
    ).rejects.toThrow(`The server did not take the upload after ${String(MAX_FAILURES)} attempts.`);
    expect(waits).toHaveLength(MAX_FAILURES - 1);
    expect(Math.max(...waits)).toBe(30_000);
  });

  it("says so when the server no longer has the upload", async () => {
    serve({
      "POST /api/images/uploads": [created()],
      [`PATCH ${slicePath}`]: [new Response(null, { status: 404 })],
    });

    await expect(
      uploadImage(file(), { signal: new AbortController().signal, wait: () => Promise.resolve() }),
    ).rejects.toThrow("The server no longer has this upload");
  });

  it("does not send again what the server refused, and gives its reason", async () => {
    const sent = serve({
      "POST /api/images/uploads": [
        new Response(JSON.stringify({ title: "The store does not have room for this file." }), {
          status: 507,
        }),
      ],
    });
    const { waits, wait } = recordingWait();

    await expect(
      uploadImage(file(), { signal: new AbortController().signal, wait }),
    ).rejects.toThrow("The store does not have room for this file.");
    expect(sent).toHaveLength(1);
    expect(waits).toEqual([]);
  });

  it("stops at once when aborted during the pause before sending again", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    serve({
      "POST /api/images/uploads": [created()],
      [`PATCH ${slicePath}`]: [new TypeError("Failed to fetch")],
    });
    const controller = new AbortController();
    let pausing: () => void = () => undefined;
    const paused = new Promise<void>((resolve) => {
      pausing = resolve;
    });

    // No wait is passed, so the pause is the real one, on the faked timers.
    const settled = uploadImage(file(), {
      signal: controller.signal,
      onProgress: (progress) => {
        if (progress.retrying) {
          pausing();
        }
      },
    }).then(
      () => "finished",
      (error: unknown) => error,
    );

    await paused;
    expect(vi.getTimerCount()).toBe(1);

    controller.abort();

    // The abort ends the pause and clears its timer; nothing waits for the timer to fire.
    expect(vi.getTimerCount()).toBe(0);
    expect(await settled).toMatchObject({ name: "AbortError", message: "The upload was stopped." });
  });
});
