// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Link,
  Outlet,
  RouterProvider,
} from "@tanstack/react-router";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import type { ImageSummary, ImageUploadSession } from "@/images/images";

import { ImagesPage } from "./ImagesPage";

const administrator: CurrentUser = {
  id: "u",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  roles: ["Administrator"],
};

const viewer: CurrentUser = { ...administrator, userName: "viewer", roles: ["Viewer"] };

function image(overrides: Partial<ImageSummary>): ImageSummary {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000a1",
    name: "Windows 11 Pro",
    kind: "Wim",
    sha256: "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0",
    sizeBytes: 2 * 1024 ** 3,
    wimIndex: 1,
    edition: "Professional",
    architecture: "x64",
    version: "10.0.26100.1",
    language: "en-US",
    installedBytes: 8 * 1024 ** 3,
    originalFileName: "install.wim",
    uploadedUtc: "2026-09-15T10:00:00Z",
    uploadedBy: "admin",
    ...overrides,
  };
}

const armImage = image({
  id: "0193a4b2-0000-7000-8000-0000000000a2",
  name: "Windows 11 Pro ARM",
  architecture: "arm64",
  language: "de-DE",
  originalFileName: "arm.wim",
});

interface Sent {
  method: string;
  path: string;
  headers: Headers;
  body: BodyInit | null | undefined;
  signal: AbortSignal | null | undefined;
}

type Handler = (request: Sent) => Response | Promise<Response>;

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers });
}

// A small server: the handlers answer "METHOD path", the current user and the CSRF token are built in,
// and everything else is a 404. Every request is recorded.
function serve(user: CurrentUser, handlers: Record<string, Handler>) {
  const requests: Sent[] = [];

  // The router scrolls to the top after a navigation, which jsdom does not implement.
  vi.stubGlobal("scrollTo", vi.fn());
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : input.toString();
      const request: Sent = {
        method: init?.method ?? "GET",
        path: url.replace("http://localhost", ""),
        headers: new Headers(init?.headers),
        body: init?.body,
        signal: init?.signal,
      };

      requests.push(request);

      if (request.path === "/api/auth/me") {
        return Promise.resolve(json(user));
      }

      if (request.path === "/api/auth/session") {
        return Promise.resolve(new Response(null, { headers: { "X-CSRF-TOKEN": "token" } }));
      }

      const handler = handlers[`${request.method} ${request.path}`];

      return Promise.resolve(
        handler === undefined ? new Response(null, { status: 404 }) : handler(request),
      );
    }),
  );

  // The page sits in a router, as in the application, next to a link to another page.
  const rootRoute = createRootRoute({ component: Shell });
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      createRoute({ getParentRoute: () => rootRoute, path: "/", component: OtherPage }),
      createRoute({ getParentRoute: () => rootRoute, path: "/images", component: ImagesPage }),
    ]),
    history: createMemoryHistory({ initialEntries: ["/images"] }),
  });

  render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  return requests;
}

function Shell() {
  return (
    <>
      <Link to="/">Machines</Link>
      <Outlet />
    </>
  );
}

function OtherPage() {
  return <h1>Machines</h1>;
}

function row(text: string): HTMLElement {
  const tableRow = screen.getByText(text).closest("tr");

  if (tableRow === null) {
    throw new Error(`${text} is not in a table row.`);
  }

  return tableRow;
}

function selectFile(file: File) {
  fireEvent.change(screen.getByLabelText("WIM or ESD file"), { target: { files: [file] } });
}

function leaveForOtherPage() {
  fireEvent.click(screen.getByRole("link", { name: "Machines" }));
}

// The server side of the upload protocol for one session: it keeps the offset it has stored and refuses
// a slice that does not start there, as the server does. A slice waits for `hold` before it is stored.
function uploadServer(options: {
  chunkBytes: number;
  failFirstSliceAfterStoring?: boolean;
  hold?: Promise<void>;
}) {
  const session: ImageUploadSession = {
    id: "0193a4b2-0000-7000-8000-0000000000u1",
    fileName: "boot.wim",
    length: 10,
    lastModified: 1_000,
    offset: 0,
    chunkBytes: options.chunkBytes,
  };
  const slices: { offset: number; bytes: number; answer: number }[] = [];
  let stored = 0;
  let failNext = options.failFirstSliceAfterStoring === true;

  const handlers: Record<string, Handler> = {
    "POST /api/images/uploads": () => json({ ...session, offset: stored }, 201),
    [`PATCH /api/images/uploads/${session.id}`]: async (request) => {
      if (options.hold !== undefined) {
        await options.hold;
      }

      const offset = Number(request.headers.get("Upload-Offset"));
      const bytes = request.body instanceof Blob ? request.body.size : -1;

      if (offset !== stored) {
        slices.push({ offset, bytes, answer: 409 });
        return new Response(null, { status: 409, headers: { "Upload-Offset": String(stored) } });
      }

      stored += bytes;

      // The server stored the slice, but its answer was lost on the way.
      if (failNext) {
        failNext = false;
        slices.push({ offset, bytes, answer: 502 });
        return new Response(null, { status: 502, headers: { "Retry-After": "0" } });
      }

      slices.push({ offset, bytes, answer: 204 });
      return new Response(null, { status: 204, headers: { "Upload-Offset": String(stored) } });
    },
  };

  return {
    session,
    slices,
    handlers,
    stored: () => stored,
  };
}

describe("ImagesPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists the images and marks the ones that cannot be deployed", async () => {
    serve(viewer, { "GET /api/images": () => json([image({}), armImage]) });

    await screen.findByText("Windows 11 Pro");

    const x64Row = within(row("Windows 11 Pro"));
    const armRow = within(row("Windows 11 Pro ARM"));

    expect(x64Row.getByText("2 GB")).toBeInTheDocument();
    expect(x64Row.getByText("8 GB")).toBeInTheDocument();
    expect(x64Row.getByText("install.wim, index 1")).toBeInTheDocument();
    expect(x64Row.getByText("0f1e2d3c4b5a")).toHaveAttribute("title", image({}).sha256);
    expect(x64Row.queryByText(/Not deployable/)).not.toBeInTheDocument();
    expect(armRow.getByText(/Not deployable/)).toBeInTheDocument();
  });

  it("offers neither upload nor delete to viewers", async () => {
    serve(viewer, { "GET /api/images": () => json([image({})]) });

    expect(await screen.findByText("Windows 11 Pro")).toBeInTheDocument();
    expect(screen.queryByLabelText("WIM or ESD file")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Delete/ })).not.toBeInTheDocument();
  });

  it("filters the list by what is typed", async () => {
    serve(viewer, { "GET /api/images": () => json([image({}), armImage]) });

    await screen.findByText("Windows 11 Pro");
    fireEvent.change(screen.getByLabelText("Filter"), { target: { value: "ARM64" } });

    expect(screen.queryByText("Windows 11 Pro")).not.toBeInTheDocument();
    expect(screen.getByText("Windows 11 Pro ARM")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Filter"), { target: { value: "de-de" } });
    expect(screen.getByText("Windows 11 Pro ARM")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Filter"), { target: { value: "server 2025" } });
    expect(screen.getByText("No image matches the filter.")).toBeInTheDocument();
  });

  it("uploads in slices, follows the offset the server names and retries what it asks to", async () => {
    const server = uploadServer({ chunkBytes: 4, failFirstSliceAfterStoring: true });
    let completed = false;
    let completeCalls = 0;

    const requests = serve(administrator, {
      ...server.handlers,
      "GET /api/images": () => json(completed ? [image({ originalFileName: "boot.wim" })] : []),
      "GET /api/images/uploads": () => json([]),
      [`POST /api/images/uploads/${server.session.id}/complete`]: () => {
        completeCalls++;

        // The first call finds the session busy, as when an earlier call is still hashing.
        if (completeCalls === 1) {
          return json({ title: "The upload is busy." }, 409, { "Retry-After": "0" });
        }

        completed = true;
        return json([image({ originalFileName: "boot.wim" })], 201);
      },
    });

    expect(await screen.findByText("No images yet")).toBeInTheDocument();

    selectFile(new File(["0123456789"], "boot.wim", { lastModified: 1_000 }));

    expect(await screen.findByText("Added 1 image from boot.wim.")).toBeInTheDocument();
    expect(await screen.findByText("Windows 11 Pro")).toBeInTheDocument();

    // 0 is stored but its answer lost; sent again, the server names 4; then 4 to 8 and 8 to 10.
    expect(server.slices).toEqual([
      { offset: 0, bytes: 4, answer: 502 },
      { offset: 0, bytes: 4, answer: 409 },
      { offset: 4, bytes: 4, answer: 204 },
      { offset: 8, bytes: 2, answer: 204 },
    ]);
    expect(server.stored()).toBe(10);
    expect(completeCalls).toBe(2);
    expect(requests.find((request) => request.method === "POST")?.body).toBe(
      JSON.stringify({ fileName: "boot.wim", length: 10, lastModified: 1_000 }),
    );
    expect(
      requests
        .filter((request) => request.method === "PATCH")
        .every((request) => request.headers.get("X-CSRF-TOKEN") === "token"),
    ).toBe(true);
  });

  it("shows the progress and Verifying while the server checks the file, and warns before leaving", async () => {
    const server = uploadServer({ chunkBytes: 8 });
    let finish: (response: Response) => void = () => undefined;

    serve(administrator, {
      ...server.handlers,
      "GET /api/images": () => json([]),
      "GET /api/images/uploads": () => json([]),
      [`POST /api/images/uploads/${server.session.id}/complete`]: () =>
        new Promise<Response>((resolve) => {
          finish = resolve;
        }),
    });

    await screen.findByText("No images yet");
    expect(screen.getByLabelText("WIM or ESD file")).toHaveAttribute("accept", ".wim,.esd");
    selectFile(new File(["0123456789"], "boot.wim", { lastModified: 1_000 }));

    expect(await screen.findByText("Verifying boot.wim")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Upload of boot.wim" })).toHaveAttribute(
      "value",
      "100",
    );
    expect(screen.getByText(/^100% of 10 bytes, .* elapsed\.$/)).toBeInTheDocument();
    expect(screen.getByLabelText("WIM or ESD file")).toBeDisabled();

    // The server goes on checking whatever the page does, so there is nothing to stop.
    expect(screen.queryByRole("button", { name: "Stop upload" })).not.toBeInTheDocument();

    const leaving = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(leaving);
    expect(leaving.defaultPrevented).toBe(true);

    leaveForOtherPage();

    const dialog = await screen.findByRole("dialog", { name: "Leave while boot.wim is checked?" });
    expect(
      within(dialog).getByText(
        "The server goes on checking boot.wim after you leave and adds its images to the library when it finishes. If it refuses the file, you do not see why.",
      ),
    ).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Close" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(screen.getByText("Verifying boot.wim")).toBeInTheDocument();

    finish(json([image({})], 200));

    expect(
      await screen.findByText("Every image in boot.wim is already in the library."),
    ).toBeInTheDocument();

    const afterwards = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(afterwards);
    expect(afterwards.defaultPrevented).toBe(false);
  });

  it("asks before a link leaves the page during an upload, and stays when told to", async () => {
    let releaseSlice: () => void = () => undefined;
    const server = uploadServer({
      chunkBytes: 16,
      hold: new Promise<void>((resolve) => {
        releaseSlice = resolve;
      }),
    });

    serve(administrator, {
      ...server.handlers,
      "GET /api/images": () => json([]),
      "GET /api/images/uploads": () => json([]),
      [`POST /api/images/uploads/${server.session.id}/complete`]: () =>
        json([image({ originalFileName: "boot.wim" })], 201),
    });

    await screen.findByText("No images yet");
    selectFile(new File(["0123456789"], "boot.wim", { lastModified: 1_000 }));
    expect(await screen.findByText("Uploading boot.wim")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Stop upload" })).toBeInTheDocument();

    leaveForOtherPage();

    const dialog = await screen.findByRole("dialog", { name: "Stop the upload of boot.wim?" });
    expect(
      within(dialog).getByText(
        "Leaving this page stops the upload of boot.wim at 0%. The server keeps what it has received, and selecting the file again on this page resumes the upload from there.",
      ),
    ).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Close" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(screen.getByRole("heading", { level: 1, name: "Images" })).toBeInTheDocument();

    // Asked again, the upload ends before anyone answers: the question goes away and the page stays.
    leaveForOtherPage();
    await screen.findByRole("dialog", { name: "Stop the upload of boot.wim?" });

    releaseSlice();

    expect(await screen.findByText("Added 1 image from boot.wim.")).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(screen.getByRole("heading", { level: 1, name: "Images" })).toBeInTheDocument();

    // The question that went away does not come back with the next upload.
    selectFile(new File(["0123456789"], "boot.wim", { lastModified: 1_000 }));
    expect(screen.getByText("Uploading boot.wim")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(await screen.findByText("Added 1 image from boot.wim.")).toBeInTheDocument();

    // With the upload over, the link leaves without asking.
    leaveForOtherPage();

    expect(await screen.findByRole("heading", { level: 1, name: "Machines" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("stops the upload when leaving the page is confirmed", async () => {
    const server = uploadServer({ chunkBytes: 16, hold: new Promise<void>(() => undefined) });

    const requests = serve(administrator, {
      ...server.handlers,
      "GET /api/images": () => json([]),
      "GET /api/images/uploads": () => json([]),
    });

    await screen.findByText("No images yet");
    selectFile(new File(["0123456789"], "boot.wim", { lastModified: 1_000 }));
    await screen.findByText("Uploading boot.wim");

    leaveForOtherPage();

    const dialog = await screen.findByRole("dialog", { name: "Stop the upload of boot.wim?" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Stop upload and leave" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Machines" })).toBeInTheDocument();

    const slice = requests.find((request) => request.method === "PATCH");
    expect(slice?.signal?.aborted).toBe(true);
  });

  it("does not call the images duplicates when an earlier complete may have added them", async () => {
    const server = uploadServer({ chunkBytes: 16 });
    let completeCalls = 0;

    serve(administrator, {
      ...server.handlers,
      "GET /api/images": () => json([]),
      "GET /api/images/uploads": () => json([]),
      [`POST /api/images/uploads/${server.session.id}/complete`]: () => {
        completeCalls++;

        // A proxy gave up on the first call while the server went on and added the images.
        return completeCalls === 1
          ? new Response(null, { status: 502, headers: { "Retry-After": "0" } })
          : json([image({ originalFileName: "boot.wim" })], 200);
      },
    });

    await screen.findByText("No images yet");
    selectFile(new File(["0123456789"], "boot.wim", { lastModified: 1_000 }));

    expect(
      await screen.findByText("The library now holds 1 image from boot.wim."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/already in the library/)).not.toBeInTheDocument();
  });

  it("shows why the server refused the file", async () => {
    const server = uploadServer({ chunkBytes: 16 });

    serve(administrator, {
      ...server.handlers,
      "GET /api/images": () => json([]),
      "GET /api/images/uploads": () => json([]),
      [`POST /api/images/uploads/${server.session.id}/complete`]: () =>
        json({ title: "This WIM holds no x64 Windows image." }, 422),
    });

    await screen.findByText("No images yet");
    selectFile(new File(["0123456789"], "boot.wim", { lastModified: 1_000 }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "boot.wim was not added. This WIM holds no x64 Windows image.",
    );
  });

  it("offers to resume an unfinished upload or to discard it", async () => {
    let sessions: ImageUploadSession[] = [
      {
        id: "0193a4b2-0000-7000-8000-0000000000u2",
        fileName: "install.wim",
        length: 1_000,
        lastModified: 1_000,
        offset: 250,
        chunkBytes: 8 * 1024 * 1024,
      },
    ];

    const requests = serve(administrator, {
      "GET /api/images": () => json([]),
      "GET /api/images/uploads": () => json(sessions),
      "DELETE /api/images/uploads/0193a4b2-0000-7000-8000-0000000000u2": () => {
        sessions = [];
        return new Response(null, { status: 204 });
      },
    });

    expect(
      await screen.findByText("Select install.wim again to resume (25%)."),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Discard install.wim" }));

    const dialog = await screen.findByRole("dialog", {
      name: "Discard the upload of install.wim?",
    });
    expect(
      within(dialog).getByText(
        "The 250 bytes of install.wim uploaded so far are deleted from the server. Selecting the file again starts the upload over.",
      ),
    ).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Discard upload" }));

    await waitFor(() => {
      expect(screen.queryByText(/again to resume/)).not.toBeInTheDocument();
    });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(
      requests.some(
        (request) =>
          request.method === "DELETE" &&
          request.path === "/api/images/uploads/0193a4b2-0000-7000-8000-0000000000u2",
      ),
    ).toBe(true);
  });

  it("deletes an image after naming it, its size and that it can no longer be assigned", async () => {
    let images = [image({}), armImage];

    const requests = serve(administrator, {
      "GET /api/images": () => json(images),
      "GET /api/images/uploads": () => json([]),
      [`DELETE /api/images/${armImage.id}`]: () => {
        images = [image({})];
        return new Response(null, { status: 204 });
      },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Delete Windows 11 Pro ARM" }));

    const dialog = await screen.findByRole("dialog", { name: "Delete Windows 11 Pro ARM?" });
    expect(
      within(dialog).getByText(
        "Windows 11 Pro ARM (2 GB) is removed from the library and can no longer be assigned to a machine. The WIM file is deleted from the server once no other image in the library comes from it.",
      ),
    ).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Delete image" }));

    await waitFor(() => {
      expect(screen.queryByText("Windows 11 Pro ARM")).not.toBeInTheDocument();
    });
    expect(requests.some((request) => request.method === "DELETE")).toBe(true);
  });

  it("keeps the dialog open with the server's reason when an image cannot be deleted", async () => {
    serve(administrator, {
      "GET /api/images": () => json([image({})]),
      "GET /api/images/uploads": () => json([]),
      [`DELETE /api/images/${image({}).id}`]: () =>
        json({ title: "A deployment that is assigned or running uses this image." }, 409),
    });

    fireEvent.click(await screen.findByRole("button", { name: "Delete Windows 11 Pro" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete image" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "A deployment that is assigned or running uses this image.",
    );
  });
});
