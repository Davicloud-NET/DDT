// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { ImageSummary, ImageUploadSession } from "@/images/images";
import { chooseMenuItem, fill, menuItems, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { administrator, imageSummary, viewer } from "@/test/builders";
import { renderPage } from "@/test/renderPage";
import { json, type Answer, type Routes } from "@/test/server";

const windows = imageSummary();

const arm = imageSummary({
  id: "0193a4b2-0000-7000-8000-0000000000a2",
  name: "Windows 11 Pro ARM",
  architecture: "arm64",
  language: "de-DE",
  originalFileName: "arm.wim",
});

const raw = imageSummary({
  id: "0193a4b2-0000-7000-8000-0000000000a3",
  name: "noble-server-cloudimg-amd64",
  kind: "RawDisk",
  sizeBytes: 600 * 1024 ** 2,
  wimIndex: 0,
  edition: null,
  version: null,
  language: null,
  installedBytes: 4 * 1024 ** 3,
  originalFileName: "noble-server-cloudimg-amd64.img",
  bootCapability: "NotSigned",
  bootDetail: "\\EFI\\BOOT\\BOOTX64.EFI carries no signature.",
  sourceSha256: "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90",
});

const signed = imageSummary({
  ...raw,
  id: "0193a4b2-0000-7000-8000-0000000000a4",
  name: "debian-12-genericcloud-amd64",
  originalFileName: "debian-12-genericcloud-amd64.raw",
  bootCapability: "SecureBootOk",
  bootDetail: "Signed under Microsoft's UEFI CA.",
});

function open(images: ImageSummary[] | Answer, routes: Routes = {}, user = administrator) {
  return renderPage({
    path: "/library/images",
    user,
    routes: {
      "GET /api/images": Array.isArray(images) ? { body: images } : images,
      "GET /api/images/uploads": { body: [] },
      ...routes,
    },
  });
}

// The row of the image with this name; the name opens its details.
function row(name: string): HTMLElement {
  const found = screen.getByRole("button", { name }).closest("tr");

  if (found === null) {
    throw new Error(`${name} is not in a row.`);
  }

  return found;
}

function search(): HTMLElement {
  return screen.getByRole("searchbox", { name: "Find an image" });
}

function selectFile(file: File) {
  const input = screen.getByRole("region", { name: "Upload" }).querySelector('input[type="file"]');

  if (input === null) {
    throw new Error("There is no file to choose.");
  }

  fireEvent.change(input, { target: { files: [file] } });
}

function bootWim(): File {
  return new File(["0123456789"], "boot.wim", { lastModified: 1_000 });
}

function leaveForOtherPage() {
  press(screen.getByRole("link", { name: "About DDT" }));
}

// The server side of the upload protocol for one session: it keeps the offset it has stored and refuses a slice
// that does not start there, as the server does. A slice waits for `hold` before it is stored.
function uploadServer(options: {
  chunkBytes: number;
  failFirstSliceAfterStoring?: boolean;
  hold?: Promise<void>;
}) {
  const session: ImageUploadSession = {
    id: "0193a4b2-0000-7000-8000-0000000000c1",
    fileName: "boot.wim",
    length: 10,
    lastModified: 1_000,
    offset: 0,
    chunkBytes: options.chunkBytes,
  };
  const slices: { offset: number; bytes: number; answer: number }[] = [];
  let stored = 0;
  let failNext = options.failFirstSliceAfterStoring === true;

  const routes: Routes = {
    "POST /api/images/uploads": () => json({ ...session, offset: stored }, 201),
    [`PATCH /api/images/uploads/${session.id}`]: async (request) => {
      if (options.hold !== undefined) {
        await options.hold;
      }

      const offset = Number(request.headers.get("Upload-Offset"));
      const bytes = request.raw instanceof Blob ? request.raw.size : -1;

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

  return { session, slices, routes, stored: () => stored };
}

describe("ImagesPage", () => {
  describe("the list", () => {
    it("lists the images and marks the ones that cannot be deployed", async () => {
      await open([windows, arm], {}, viewer);

      await screen.findByRole("button", { name: "Windows 11 Pro" });
      const x64 = within(row("Windows 11 Pro"));
      const armRow = within(row("Windows 11 Pro ARM"));

      expect(x64.getByText("install.wim, index 1")).toBeInTheDocument();
      expect(x64.getByText("Windows image, x64, 10.0.26100.1, en-US")).toBeInTheDocument();
      expect(x64.getByText("2 GB")).toBeInTheDocument();
      expect(x64.getByText("8 GB installed")).toBeInTheDocument();
      expect(x64.getByText("Microsoft's boot files")).toBeInTheDocument();
      expect(x64.queryByText(/Not deployable/)).not.toBeInTheDocument();
      expect(
        armRow.getByText("Not deployable: only x64 images can be installed."),
      ).toBeInTheDocument();
    });

    it("names each image's kind and whether a raw disk image starts with Secure Boot on", async () => {
      await open([windows, raw, signed], {}, viewer);

      await screen.findByRole("button", { name: "Windows 11 Pro" });
      const rawRow = within(row("noble-server-cloudimg-amd64"));

      expect(rawRow.getByText("Raw disk image, x64")).toBeInTheDocument();
      expect(rawRow.getByText("noble-server-cloudimg-amd64.img")).toBeInTheDocument();
      expect(rawRow.getByText("600 MB")).toBeInTheDocument();
      expect(rawRow.getByText("4 GB disk")).toBeInTheDocument();
      expect(rawRow.getByText("Not signed")).toBeInTheDocument();
      expect(rawRow.queryByText(/Not deployable/)).not.toBeInTheDocument();
      expect(within(row("debian-12-genericcloud-amd64")).getByText("Signed")).toBeInTheDocument();

      fill(search(), "not signed");
      await waitFor(() => {
        expect(screen.queryByRole("button", { name: "Windows 11 Pro" })).not.toBeInTheDocument();
      });
      expect(
        screen.queryByRole("button", { name: "debian-12-genericcloud-amd64" }),
      ).not.toBeInTheDocument();
      expect(
        screen.getByRole("button", { name: "noble-server-cloudimg-amd64" }),
      ).toBeInTheDocument();

      fill(search(), "raw disk");
      expect(
        await screen.findByRole("button", { name: "debian-12-genericcloud-amd64" }),
      ).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Windows 11 Pro" })).not.toBeInTheDocument();
    });

    it("shows an image's details, with why a raw disk image may not start", async () => {
      await open([windows, raw], {}, viewer);

      press(await screen.findByRole("button", { name: "noble-server-cloudimg-amd64" }));

      const details = await screen.findByRole("dialog", { name: "noble-server-cloudimg-amd64" });
      expect(
        within(details).getByText("\\EFI\\BOOT\\BOOTX64.EFI carries no signature."),
      ).toBeInTheDocument();
      expect(
        within(details).getByText(/^This image will not start with Secure Boot on\./),
      ).toBeInTheDocument();
      expect(within(details).getByText(raw.sourceSha256 ?? "")).toBeInTheDocument();
      expect(within(details).getByText(raw.sha256)).toBeInTheDocument();

      press(within(details).getByRole("button", { name: "Close" }));
      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
    });

    it("filters the list by what is typed", async () => {
      await open([windows, arm], {}, viewer);

      await screen.findByRole("button", { name: "Windows 11 Pro" });
      fill(search(), "ARM64");

      await waitFor(() => {
        expect(screen.queryByRole("button", { name: "Windows 11 Pro" })).not.toBeInTheDocument();
      });
      expect(screen.getByRole("button", { name: "Windows 11 Pro ARM" })).toBeInTheDocument();

      fill(search(), "de-de");
      expect(screen.getByRole("button", { name: "Windows 11 Pro ARM" })).toBeInTheDocument();

      fill(search(), "server 2025");
      expect(await screen.findByText("No image matches")).toBeInTheDocument();
    });

    it("offers neither upload nor delete to viewers", async () => {
      await open([windows], {}, viewer);

      await screen.findByRole("button", { name: "Windows 11 Pro" });
      expect(screen.queryByRole("region", { name: "Upload" })).not.toBeInTheDocument();
      expect(
        await menuItems(screen.getByRole("button", { name: "Actions for Windows 11 Pro" })),
      ).toEqual(["Details", "Copy SHA-256"]);
    });

    it("says what an empty library is for", async () => {
      await open([]);

      expect(await screen.findByText("No images yet")).toBeInTheDocument();
      expect(screen.getByText(/^Upload a WIM file to add its Windows images/)).toBeInTheDocument();
    });

    it("reads the list again when the hub says the images changed", async () => {
      let images = [windows];
      const { hub, server } = await open(() => json(images));

      await screen.findByRole("button", { name: "Windows 11 Pro" });
      images = [windows, arm];

      act(() => {
        hub?.push("imagesChanged");
      });

      expect(await screen.findByRole("button", { name: "Windows 11 Pro ARM" })).toBeInTheDocument();
      expect(server.count("GET /api/images")).toBe(2);
    });
  });

  describe("uploads", () => {
    it("uploads in slices, follows the offset the server names and retries what it asks to", async () => {
      const upload = uploadServer({ chunkBytes: 4, failFirstSliceAfterStoring: true });
      let completeCalls = 0;

      const { server } = await open([], {
        ...upload.routes,
        [`POST /api/images/uploads/${upload.session.id}/complete`]: () => {
          completeCalls++;

          // The first call finds the session busy, as when an earlier call is still hashing.
          return completeCalls === 1
            ? json({ title: "The upload is busy." }, 409, { "Retry-After": "0" })
            : json([imageSummary({ originalFileName: "boot.wim" })], 201);
        },
      });

      expect(await screen.findByText("No images yet")).toBeInTheDocument();
      selectFile(bootWim());

      expect(await screen.findByText("Added 1 image from boot.wim.")).toBeInTheDocument();
      // The image comes from the server's answer, so the list is not read again.
      expect(await screen.findByRole("button", { name: "Windows 11 Pro" })).toBeInTheDocument();
      expect(server.count("GET /api/images")).toBe(1);

      // 0 is stored but its answer lost; sent again, the server names 4; then 4 to 8 and 8 to 10.
      expect(upload.slices).toEqual([
        { offset: 0, bytes: 4, answer: 502 },
        { offset: 0, bytes: 4, answer: 409 },
        { offset: 4, bytes: 4, answer: 204 },
        { offset: 8, bytes: 2, answer: 204 },
      ]);
      expect(upload.stored()).toBe(10);
      expect(completeCalls).toBe(2);
      expect(
        server.requests.find(
          (request) => request.method === "POST" && request.path === "/api/images/uploads",
        )?.body,
      ).toEqual({ fileName: "boot.wim", length: 10, lastModified: 1_000 });
      expect(
        server.requests
          .filter((request) => request.method === "PATCH")
          .every((request) => request.headers.get("X-CSRF-TOKEN") === "token"),
      ).toBe(true);
    });

    it("shows the progress and Checking while the server checks the file, and warns before leaving", async () => {
      const upload = uploadServer({ chunkBytes: 8 });
      let finish: (response: Response) => void = () => undefined;

      await open([], {
        ...upload.routes,
        [`POST /api/images/uploads/${upload.session.id}/complete`]: () =>
          new Promise<Response>((resolve) => {
            finish = resolve;
          }),
      });

      await screen.findByText("No images yet");
      expect(
        screen.getByRole("region", { name: "Upload" }).querySelector('input[type="file"]'),
      ).toHaveAttribute("accept", ".wim,.esd,.img,.raw,.gz,.xz,.zst,.qcow2");
      selectFile(bootWim());

      const progress = await screen.findByRole("progressbar", { name: "Checking boot.wim" });
      expect(progress).toHaveAttribute("aria-valuenow", "100");
      expect(screen.getByText(/^100% of 10 bytes, .* so far$/)).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Choose a file" })).not.toBeInTheDocument();

      // The server goes on checking whatever the page does, so there is nothing to stop.
      expect(screen.queryByRole("button", { name: "Stop upload" })).not.toBeInTheDocument();

      const leaving = new Event("beforeunload", { cancelable: true });
      window.dispatchEvent(leaving);
      expect(leaving.defaultPrevented).toBe(true);

      leaveForOtherPage();

      const dialog = await screen.findByRole("dialog", {
        name: "Leave while boot.wim is checked?",
      });
      expect(dialog).toHaveAccessibleDescription(
        "The server goes on checking boot.wim after you leave and adds its images to the library when it finishes. If it refuses the file, you do not see why.",
      );

      press(within(dialog).getByRole("button", { name: "Cancel" }));

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(screen.getByRole("progressbar", { name: "Checking boot.wim" })).toBeInTheDocument();

      finish(json([windows], 200));

      expect(
        await screen.findByText("Every image in boot.wim is already in the library."),
      ).toBeInTheDocument();

      const afterwards = new Event("beforeunload", { cancelable: true });
      window.dispatchEvent(afterwards);
      expect(afterwards.defaultPrevented).toBe(false);
    });

    it("asks before a link leaves the page during an upload, and stays when told to", async () => {
      let releaseSlice: () => void = () => undefined;
      const upload = uploadServer({
        chunkBytes: 16,
        hold: new Promise<void>((resolve) => {
          releaseSlice = resolve;
        }),
      });

      await open([], {
        ...upload.routes,
        [`POST /api/images/uploads/${upload.session.id}/complete`]: () =>
          json([imageSummary({ originalFileName: "boot.wim" })], 201),
      });

      await screen.findByText("No images yet");
      selectFile(bootWim());
      expect(
        await screen.findByRole("progressbar", { name: "Uploading boot.wim" }),
      ).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Stop upload" })).toBeInTheDocument();

      leaveForOtherPage();

      const dialog = await screen.findByRole("dialog", { name: "Stop the upload of boot.wim?" });
      expect(dialog).toHaveAccessibleDescription(
        "Leaving this page stops the upload of boot.wim at 0%. The server keeps what it has received, and choosing the file again here resumes the upload from there.",
      );

      press(within(dialog).getByRole("button", { name: "Cancel" }));

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(screen.getByRole("heading", { level: 1, name: "OS images" })).toBeInTheDocument();

      // Asked again, the upload ends before anyone answers: the question goes away and the page stays.
      leaveForOtherPage();
      await screen.findByRole("dialog", { name: "Stop the upload of boot.wim?" });

      releaseSlice();

      expect(await screen.findByText("Added 1 image from boot.wim.")).toBeInTheDocument();
      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(screen.getByRole("heading", { level: 1, name: "OS images" })).toBeInTheDocument();

      // The question that went away does not come back with the next upload.
      selectFile(bootWim());
      expect(screen.getByRole("progressbar", { name: "Uploading boot.wim" })).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(await screen.findByText("Added 1 image from boot.wim.")).toBeInTheDocument();

      // With the upload over, the link leaves without asking.
      leaveForOtherPage();

      expect(
        await screen.findByRole("heading", { level: 1, name: "About DDT" }),
      ).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    it("stops the upload when leaving the page is confirmed", async () => {
      const upload = uploadServer({ chunkBytes: 16, hold: new Promise<void>(() => undefined) });
      const { server } = await open([], upload.routes);

      await screen.findByText("No images yet");
      selectFile(bootWim());
      await screen.findByRole("progressbar", { name: "Uploading boot.wim" });

      leaveForOtherPage();

      const dialog = await screen.findByRole("dialog", { name: "Stop the upload of boot.wim?" });
      press(within(dialog).getByRole("button", { name: "Stop upload and leave" }));

      expect(
        await screen.findByRole("heading", { level: 1, name: "About DDT" }),
      ).toBeInTheDocument();
      expect(server.requests.find((request) => request.method === "PATCH")?.signal?.aborted).toBe(
        true,
      );
    });

    it("stops an upload on request and says how to resume it", async () => {
      const upload = uploadServer({ chunkBytes: 16, hold: new Promise<void>(() => undefined) });
      const { server } = await open([], upload.routes);

      await screen.findByText("No images yet");
      selectFile(bootWim());
      await screen.findByRole("progressbar", { name: "Uploading boot.wim" });

      press(screen.getByRole("button", { name: "Stop upload" }));

      expect(
        await screen.findByText("The upload of boot.wim stopped. Choose the file again to resume."),
      ).toBeInTheDocument();
      expect(server.requests.find((request) => request.method === "PATCH")?.signal?.aborted).toBe(
        true,
      );
      expect(screen.getByRole("button", { name: "Choose a file" })).toBeInTheDocument();
    });

    it("does not call the images duplicates when an earlier complete may have added them", async () => {
      const upload = uploadServer({ chunkBytes: 16 });
      let completeCalls = 0;

      await open([], {
        ...upload.routes,
        [`POST /api/images/uploads/${upload.session.id}/complete`]: () => {
          completeCalls++;

          // A proxy gave up on the first call while the server went on and added the images.
          return completeCalls === 1
            ? new Response(null, { status: 502, headers: { "Retry-After": "0" } })
            : json([imageSummary({ originalFileName: "boot.wim" })], 200);
        },
      });

      await screen.findByText("No images yet");
      selectFile(bootWim());

      expect(
        await screen.findByText("The library now holds 1 image from boot.wim."),
      ).toBeInTheDocument();
      expect(screen.queryByText(/already in the library/)).not.toBeInTheDocument();
    });

    it("shows why the server refused the file", async () => {
      const upload = uploadServer({ chunkBytes: 16 });

      await open([], {
        ...upload.routes,
        [`POST /api/images/uploads/${upload.session.id}/complete`]: () =>
          json({ title: "This WIM holds no x64 Windows image." }, 422),
      });

      await screen.findByText("No images yet");
      selectFile(bootWim());

      expect(
        await screen.findByText("boot.wim was not added. This WIM holds no x64 Windows image."),
      ).toBeInTheDocument();
    });

    it("says that an empty file is not uploaded", async () => {
      const { server } = await open([]);

      await screen.findByText("No images yet");
      selectFile(new File([], "empty.wim"));

      expect(await screen.findByText("empty.wim is empty.")).toBeInTheDocument();
      expect(server.changes()).toEqual([]);
    });

    it("offers to resume an unfinished upload or to discard it", async () => {
      const unfinished: ImageUploadSession = {
        id: "0193a4b2-0000-7000-8000-0000000000c2",
        fileName: "install.wim",
        length: 1_000,
        lastModified: 1_000,
        offset: 250,
        chunkBytes: 8 * 1024 * 1024,
      };
      const { server } = await open([], {
        "GET /api/images/uploads": { body: [unfinished] },
        [`DELETE /api/images/uploads/${unfinished.id}`]: { status: 204 },
      });

      expect(
        await screen.findByText("Choose install.wim again to resume at 25%."),
      ).toBeInTheDocument();

      press(screen.getByRole("button", { name: "Discard install.wim" }));

      const dialog = await screen.findByRole("dialog", {
        name: "Discard the upload of install.wim?",
      });
      expect(dialog).toHaveAccessibleDescription(
        "The 250 bytes of install.wim uploaded so far are deleted from the server. Choosing the file again starts the upload over.",
      );

      press(within(dialog).getByRole("button", { name: "Discard upload" }));

      await waitFor(() => {
        expect(screen.queryByText(/again to resume/)).not.toBeInTheDocument();
      });
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
        `DELETE /api/images/uploads/${unfinished.id}`,
      ]);
      expect(server.count("GET /api/images/uploads")).toBe(1);
    });
  });

  describe("deleting", () => {
    it("deletes an image after naming it, its size and that no sequence can use it any more", async () => {
      const { server } = await open([windows, arm], {
        [`DELETE /api/images/${arm.id}`]: { status: 204 },
      });

      await screen.findByRole("button", { name: "Windows 11 Pro ARM" });
      await chooseMenuItem(
        screen.getByRole("button", { name: "Actions for Windows 11 Pro ARM" }),
        "Delete",
      );

      const dialog = await screen.findByRole("dialog", { name: "Delete Windows 11 Pro ARM?" });
      expect(dialog).toHaveAccessibleDescription(
        "Windows 11 Pro ARM (2 GB) is removed from the library and can no longer be used by a task sequence. The WIM file is deleted from the server once no other image in the library comes from it.",
      );

      press(within(dialog).getByRole("button", { name: "Delete image" }));

      await waitFor(() => {
        expect(
          screen.queryByRole("button", { name: "Windows 11 Pro ARM" }),
        ).not.toBeInTheDocument();
      });
      expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
        `DELETE /api/images/${arm.id}`,
      ]);
      expect(server.count("GET /api/images")).toBe(1);
    });

    it("says that deleting a raw disk image deletes its stored disk", async () => {
      await open([raw]);

      await chooseMenuItem(
        await screen.findByRole("button", { name: "Actions for noble-server-cloudimg-amd64" }),
        "Delete",
      );

      expect(
        await screen.findByRole("dialog", { name: "Delete noble-server-cloudimg-amd64?" }),
      ).toHaveAccessibleDescription(
        "noble-server-cloudimg-amd64 (600 MB) is removed from the library and can no longer be used by a task sequence. Its compressed disk is deleted from the server.",
      );
    });

    it("keeps the dialog open with the server's reason when an image cannot be deleted", async () => {
      await open([windows], {
        [`DELETE /api/images/${windows.id}`]: {
          status: 409,
          body: { title: "A deployment that is assigned or running uses this image." },
        },
      });

      await chooseMenuItem(
        await screen.findByRole("button", { name: "Actions for Windows 11 Pro" }),
        "Delete",
      );
      const dialog = await screen.findByRole("dialog");
      press(within(dialog).getByRole("button", { name: "Delete image" }));

      expect(await within(dialog).findByRole("alert")).toHaveTextContent(
        "A deployment that is assigned or running uses this image.",
      );
    });
  });

  describe("accessibility", () => {
    it("has no violations with images listed and the upload panel shown", async () => {
      await open([windows, arm, raw], {
        "GET /api/images/uploads": {
          body: [
            {
              id: "0193a4b2-0000-7000-8000-0000000000c2",
              fileName: "install.wim",
              length: 1_000,
              lastModified: 1_000,
              offset: 250,
              chunkBytes: 8,
            },
          ],
        },
      });

      await screen.findByText("Choose install.wim again to resume at 25%.");
      await expectNoAxeViolations();
    });

    it("has no violations with an empty library", async () => {
      await open([], {}, viewer);

      await screen.findByText("No images yet");
      await expectNoAxeViolations();
    });

    it("has no violations with an image's details open", async () => {
      await open([raw]);

      press(await screen.findByRole("button", { name: "noble-server-cloudimg-amd64" }));
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });

    it("has no violations with the delete dialog open", async () => {
      await open([windows]);

      await chooseMenuItem(
        await screen.findByRole("button", { name: "Actions for Windows 11 Pro" }),
        "Delete",
      );
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });

    it("has no violations while a file uploads", async () => {
      const upload = uploadServer({ chunkBytes: 16, hold: new Promise<void>(() => undefined) });
      await open([], upload.routes);

      await screen.findByText("No images yet");
      selectFile(bootWim());
      await screen.findByRole("progressbar", { name: "Uploading boot.wim" });
      await expectNoAxeViolations();
    });
  });
});
