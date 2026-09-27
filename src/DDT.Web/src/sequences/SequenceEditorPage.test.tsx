// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import type { ImageSummary } from "@/images/images";

import { SequenceEditorPage } from "./SequenceEditorPage";
import {
  SEQUENCE_VERSION,
  type RunScriptStep,
  type SaveSequenceRequest,
  type SequenceProblem,
  type SequenceStep,
  type SequenceView,
} from "./sequences";
import { sequenceSearch } from "./sequenceSearch";
import { EMPTY_ID, newStep } from "./steps";

const administrator: CurrentUser = {
  id: "u",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  mustChangePassword: false,
  roles: ["Administrator"],
};

const viewer: CurrentUser = { ...administrator, userName: "viewer", roles: ["Viewer"] };

const sequenceId = "0193a4b2-0000-7000-8000-0000000000e1";

const image: ImageSummary = {
  id: "0193a4b2-0000-7000-8000-0000000000a1",
  name: "Windows 11 Pro",
  kind: "Wim",
  sha256: "00",
  sizeBytes: 1,
  wimIndex: 1,
  edition: "Professional",
  architecture: "x64",
  version: "10.0.26100.1",
  language: "en-US",
  installedBytes: 1,
  originalFileName: "install.wim",
  uploadedUtc: "2026-09-15T10:00:00Z",
  uploadedBy: "admin",
  bootCapability: null,
  bootDetail: null,
  sourceSha256: null,
};

const rawImage: ImageSummary = {
  ...image,
  id: "0193a4b2-0000-7000-8000-0000000000a2",
  name: "noble",
  kind: "RawDisk",
  wimIndex: 0,
  edition: null,
  version: null,
  language: null,
  originalFileName: "noble.img",
  bootCapability: "NotSigned",
  bootDetail: "\\EFI\\BOOT\\BOOTX64.EFI carries no signature.",
  sourceSha256: "01",
};

const signedRawImage: ImageSummary = {
  ...rawImage,
  id: "0193a4b2-0000-7000-8000-0000000000a3",
  name: "debian-12",
  bootCapability: "SecureBootOk",
  bootDetail: "Signed under Microsoft's UEFI CA.",
};

const steps: SequenceStep[] = [
  newStep("partition", "p"),
  newStep("applyImage", "i"),
  {
    ...(newStep("runScript", "s") as RunScriptStep),
    name: "Set wallpaper",
    script: "exit 0",
    conditions: [{ variable: "Model", operator: "Equals", value: "" }],
  },
];

function view(overrides: Partial<SequenceView> = {}): SequenceView {
  return {
    id: sequenceId,
    name: "Lab PCs",
    description: null,
    revision: 3,
    definition: { version: 1, steps },
    stepPhases: ["WindowsPE", "WindowsPE", "WindowsPE"],
    problems: [],
    warnings: [],
    updatedUtc: "2026-09-16T10:00:00Z",
    updatedBy: "admin",
    ...overrides,
  };
}

const linuxView = (imageId = EMPTY_ID) =>
  view({
    definition: {
      version: SEQUENCE_VERSION,
      steps: [
        { ...newStep("writeRawImage", "w"), imageId } as SequenceStep,
        newStep("writeCloudInitSeed", "c"),
      ],
    },
    stepPhases: ["WindowsPE", "WindowsPE"],
  });

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

type SaveAnswer = (request: SaveSequenceRequest) => Response;

// The server holds one sequence. Saves answer as the given function says; by default they are stored with the
// next revision, as the server does.
function serve(
  user: CurrentUser,
  initial: SequenceView,
  {
    answer,
    images = [image],
    step,
  }: { answer?: SaveAnswer; images?: ImageSummary[]; step?: string } = {},
) {
  let stored = initial;
  let deleted = false;
  const saves: SaveSequenceRequest[] = [];
  const reads: string[] = [];

  const store: SaveAnswer = (request) => {
    stored = {
      ...stored,
      name: request.name,
      description: request.description,
      definition: request.definition,
      revision: stored.revision + 1,
      updatedBy: user.userName,
    };
    return json(stored);
  };

  vi.stubGlobal("scrollTo", vi.fn());
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const path = (input instanceof Request ? input.url : input.toString()).replace(
        "http://localhost",
        "",
      );
      const method = init?.method ?? "GET";

      if (method === "GET") {
        reads.push(path);
      }

      switch (`${method} ${path}`) {
        case "GET /api/auth/me":
          return Promise.resolve(json(user));
        case "GET /api/auth/session":
          return Promise.resolve(new Response(null, { headers: { "X-CSRF-TOKEN": "token" } }));
        case `GET /api/sequences/${sequenceId}`:
          return Promise.resolve(deleted ? new Response(null, { status: 404 }) : json(stored));
        case `PUT /api/sequences/${sequenceId}`: {
          const body = typeof init?.body === "string" ? init.body : "null";
          const request = JSON.parse(body) as SaveSequenceRequest;
          saves.push(request);

          return Promise.resolve((answer ?? store)(request));
        }
        case "GET /api/images":
          return Promise.resolve(json(images));
        case "GET /api/packages":
          return Promise.resolve(json([]));
        case "GET /api/deployments/options":
          return Promise.resolve(
            json({
              domainConfigured: false,
              requireWebApproval: false,
              zeroTouchEnabled: false,
              serverUtc: "2026-09-16T10:00:00Z",
            }),
          );
        default:
          return Promise.resolve(new Response(null, { status: 404 }));
      }
    }),
  );

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const rootRoute = createRootRoute();
  const shellRoute = createRoute({ getParentRoute: () => rootRoute, id: "shell" });
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      shellRoute.addChildren([
        createRoute({
          getParentRoute: () => shellRoute,
          path: "/deployment/sequences",
          component: () => <p>All the sequences</p>,
        }),
        createRoute({
          getParentRoute: () => shellRoute,
          path: "/deployment/sequences/$sequenceId",
          validateSearch: sequenceSearch,
          component: SequenceEditorPage,
        }),
      ]),
    ]),
    history: createMemoryHistory({
      initialEntries: [
        `/deployment/sequences/${sequenceId}${step === undefined ? "" : `?step=${step}`}`,
      ],
    }),
  });

  render(
    <I18nProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </I18nProvider>,
  );

  const remove = () => {
    deleted = true;
  };

  return { saves, reads, queryClient, router, remove };
}

// The debounce of typing is 700 ms.
const saveWait = { timeout: 3_000 };

async function opened() {
  return screen.findByRole("heading", { level: 1, name: "Lab PCs" });
}

// The element that holds a field of the step shown, by the name the server's findings give it.
function field(name: string): HTMLElement {
  const slot = document.querySelector<HTMLElement>(`[data-field="${name}"]`);

  if (slot === null) {
    throw new Error(`No ${name} field shows.`);
  }

  return slot;
}

// A list opens only once it has something to choose from, such as the images, which load after the sequence.
async function loaded(name: string) {
  await waitFor(() => {
    expect(field(name).querySelectorAll("select option").length).toBeGreaterThan(1);
  });
}

// Chooses in a list as a person would: the list opens, and the option is picked.
async function choose(name: string, option: string) {
  await loaded(name);
  fireEvent.click(within(field(name)).getByRole("button"));
  fireEvent.click(await screen.findByRole("option", { name: new RegExp(`^${option}`) }));
}

// The steps in the order the rail shows them.
const order = () =>
  within(screen.getByRole("listbox", { name: /^Steps of / }))
    .getAllByRole("option")
    .map((option) => option.getAttribute("data-key"));

function railStep(id: string): HTMLElement {
  const option = within(screen.getByRole("listbox", { name: /^Steps of / }))
    .getAllByRole("option")
    .find((candidate) => candidate.getAttribute("data-key") === id);

  if (option === undefined) {
    throw new Error(`The rail has no step ${id}.`);
  }

  return option;
}

describe("SequenceEditorPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("marks the steps with findings on the rail, shows them at their fields and sums them up", async () => {
    const problems: SequenceProblem[] = [
      { stepId: "i", field: "imageId", message: "Choose the image to apply." },
      { stepId: "s", field: "conditions[0].value", message: "Enter the value to compare with." },
      { stepId: "s", field: null, message: "A step in Windows needs an earlier step." },
    ];
    const warnings: SequenceProblem[] = [
      { stepId: null, field: null, message: "No step adds the local administrator." },
    ];

    serve(administrator, view({ problems, warnings }), { step: "i" });

    await opened();
    expect(screen.getByText("Cannot run")).toBeInTheDocument();
    expect(railStep("i")).toHaveAccessibleName("Step 2, Apply image, 1 problem");
    expect(railStep("s")).toHaveAccessibleName("Step 3, Set wallpaper, Run script, 2 problems");
    expect(railStep("p")).toHaveAccessibleName("Step 1, Partition the disk");
    expect(
      screen.getByText("3 problems keep it from running. 1 warning, which does not."),
    ).toBeInTheDocument();
    expect(screen.getByText("No step adds the local administrator.")).toBeInTheDocument();

    expect(within(field("imageId")).getByRole("button")).toHaveAccessibleDescription(
      /Choose the image to apply\./,
    );

    // A finding in the summary shows its step and takes the focus to its field.
    fireEvent.click(
      screen.getByRole("button", {
        name: "Enter the value to compare with. Go to step 03, Set wallpaper.",
      }),
    );

    const value = await screen.findByRole("textbox", { name: "Value of condition 1" });
    await waitFor(() => {
      expect(value).toHaveFocus();
    });
    expect(value).toHaveAccessibleDescription("Enter the value to compare with.");
    expect(railStep("s")).toHaveAttribute("aria-selected", "true");
    expect(
      within(screen.getByRole("list", { name: "Problems and warnings of this step" })).getByText(
        "A step in Windows needs an earlier step.",
      ),
    ).toBeInTheDocument();
  });

  it("saves an edit in place with the revision it read", async () => {
    const { saves } = serve(administrator, view(), { step: "i" });

    await opened();
    fireEvent.change(screen.getByRole("textbox", { name: "Description" }), {
      target: { value: "For room 4" },
    });
    await choose("imageId", "Windows 11 Pro");

    expect(screen.getByText("Unsaved changes")).toBeInTheDocument();
    await waitFor(() => {
      expect(saves).toHaveLength(1);
    }, saveWait);

    expect(saves[0]).toMatchObject({
      revision: 3,
      name: "Lab PCs",
      description: "For room 4",
      // The page sends the highest version it knows; the server stores the lowest the steps need.
      definition: { version: SEQUENCE_VERSION },
    });
    expect(saves[0]?.definition.steps[1]).toMatchObject({ kind: "applyImage", imageId: image.id });
    expect(await screen.findByText(/^All changes saved at /)).toBeInTheDocument();

    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab PCs, room 4" },
    });

    await waitFor(() => {
      expect(saves).toHaveLength(2);
    }, saveWait);
    expect(saves[1]).toMatchObject({ revision: 4, name: "Lab PCs, room 4" });
  });

  it("shows the server's refusal of a name at the field", async () => {
    serve(administrator, view(), {
      answer: () =>
        json(
          { title: "Invalid", errors: { name: ["Another sequence is already called Lab."] } },
          400,
        ),
    });

    await opened();
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });

    await waitFor(() => {
      expect(screen.getByRole("textbox", { name: "Sequence name" })).toHaveAccessibleDescription(
        "Another sequence is already called Lab.",
      );
    }, saveWait);
    expect(
      screen.getByText("Not saved: Another sequence is already called Lab."),
    ).toBeInTheDocument();
  });

  it("keeps numbers the server cannot store out of the document", async () => {
    const { saves } = serve(administrator, view(), { step: "s" });

    await opened();
    const codes = screen.getByRole("textbox", { name: "Exit codes that mean success" });
    fireEvent.change(codes, { target: { value: "0, 3000000000" } });

    expect(
      screen.getByText(
        "Enter whole numbers from -2147483648 to 2147483647, separated by commas. Until then the last list stays.",
      ),
    ).toBeInTheDocument();

    const timeout = within(field("timeoutMinutes")).getByRole("textbox");
    fireEvent.change(timeout, { target: { value: "3000000000" } });
    fireEvent.blur(timeout);

    await waitFor(() => {
      expect(saves).toHaveLength(1);
    }, saveWait);
    expect(saves[0]?.definition.steps[2]).toMatchObject({
      timeoutMinutes: 2_147_483_647,
      successExitCodes: [0],
    });
  });

  it("moves a step with the keyboard, keeps the focus on it, says where it went and saves the order", async () => {
    const { saves } = serve(administrator, view(), { step: "i" });

    await opened();
    const handle = screen.getByRole("button", { name: "Move Apply image" });
    act(() => {
      handle.focus();
    });
    fireEvent.keyDown(handle, { key: "ArrowUp" });

    expect(order()).toEqual(["i", "p", "s"]);
    expect(screen.getByRole("button", { name: "Move Apply image" })).toHaveFocus();
    expect(screen.getByText("Apply image moved to position 1 of 3.")).toBeInTheDocument();

    const name = screen.getByRole("textbox", { name: "Name" });
    act(() => {
      name.focus();
    });
    fireEvent.keyDown(name, { key: "ArrowDown", altKey: true });
    fireEvent.keyDown(name, { key: "ArrowDown", altKey: true });

    expect(order()).toEqual(["p", "s", "i"]);
    expect(screen.getByRole("textbox", { name: "Name" })).toHaveFocus();
    expect(screen.getByText("Apply image moved to position 3 of 3.")).toBeInTheDocument();

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.id)).toEqual(["p", "s", "i"]);
    }, saveWait);
  });

  it("moves a step to the first or the last place with Home and End on its Move key", async () => {
    serve(administrator, view(), { step: "s" });

    await opened();
    fireEvent.keyDown(screen.getByRole("button", { name: "Move Set wallpaper" }), { key: "Home" });
    expect(order()).toEqual(["s", "p", "i"]);

    fireEvent.keyDown(screen.getByRole("button", { name: "Move Set wallpaper" }), { key: "End" });
    expect(order()).toEqual(["p", "i", "s"]);
  });

  it("moves the focused step on the rail with Alt and an arrow key", async () => {
    serve(administrator, view(), { step: "p" });

    await opened();
    const first = railStep("p");
    act(() => {
      first.focus();
    });
    fireEvent.keyDown(first, { key: "ArrowRight", altKey: true });

    expect(order()).toEqual(["i", "p", "s"]);
    await waitFor(() => {
      expect(railStep("p")).toHaveFocus();
    });
    expect(screen.getByText("Partition the disk moved to position 2 of 3.")).toBeInTheDocument();
  });

  it("names who saved in between, and keeps this page's version only once confirmed", async () => {
    const theirs = view({ revision: 4, name: "Lab PCs (bob)", updatedBy: "bob" });
    let conflicted = false;
    const { saves } = serve(administrator, view(), {
      answer: (request) => {
        if (!conflicted) {
          conflicted = true;
          return json(theirs, 409);
        }

        return json({ ...theirs, name: request.name, revision: 5, updatedBy: "admin" });
      },
    });

    await opened();
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab PCs (mine)" },
    });

    const title = await screen.findByText(
      /^bob saved this sequence at .* while you were editing\.$/,
      undefined,
      saveWait,
    );
    const notice = title.closest<HTMLElement>("[role=status]");

    if (notice === null) {
      throw new Error("The conflict is not in its notice.");
    }

    expect(notice).toHaveTextContent("They changed the name.");
    expect(
      screen.getByText("Not saved: another administrator saved this sequence"),
    ).toBeInTheDocument();

    fireEvent.click(within(notice).getByRole("button", { name: "Keep mine" }));
    const dialog = await screen.findByRole("dialog", { name: "Save your version over theirs?" });
    expect(dialog).toHaveTextContent(
      /Your version replaces the one bob saved at .*\. Their changes to the name are lost\./,
    );
    fireEvent.click(within(dialog).getByRole("button", { name: "Save my version" }));

    await waitFor(() => {
      expect(saves).toHaveLength(2);
    });
    expect(saves[1]).toMatchObject({ revision: 4, name: "Lab PCs (mine)" });
    expect(await screen.findByText(/^All changes saved at /)).toBeInTheDocument();
  });

  it("takes the other version and drops this page's edits, without reading it again", async () => {
    const theirs = view({ revision: 4, name: "Lab PCs (bob)", updatedBy: "bob" });
    const { saves, reads } = serve(administrator, view(), { answer: () => json(theirs, 409) });

    await opened();
    await waitFor(() => {
      expect(screen.getByText("All changes saved")).toBeInTheDocument();
    });
    const readsBefore = reads.length;
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab PCs (mine)" },
    });

    const takeTheirs = await screen.findByRole("button", { name: "Use theirs" }, saveWait);
    await waitFor(() => {
      expect(takeTheirs).toBeEnabled();
    });
    fireEvent.click(takeTheirs);

    expect(screen.getByRole("textbox", { name: "Sequence name" })).toHaveValue("Lab PCs (bob)");
    expect(screen.queryByRole("button", { name: "Use theirs" })).not.toBeInTheDocument();
    expect(saves).toHaveLength(1);
    // Their copy came with the refusal.
    expect(reads.slice(readsBefore)).not.toContain(`/api/sequences/${sequenceId}`);
  });

  it("saves at once on leaving, and goes once it is saved", async () => {
    const { saves } = serve(administrator, view());

    await opened();
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });
    fireEvent.click(screen.getByRole("link", { name: "Task sequences" }));

    expect(await screen.findByText("All the sequences")).toBeInTheDocument();
    expect(saves).toEqual([expect.objectContaining({ name: "Lab", revision: 3 })]);
  });

  it("asks before leaving when the changes cannot be saved", async () => {
    serve(administrator, view(), {
      answer: () => json(view({ revision: 4, updatedBy: "bob" }), 409),
    });

    await opened();
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });
    fireEvent.click(screen.getByRole("link", { name: "Task sequences" }));

    const dialog = await screen.findByRole("dialog", { name: "Leave without saving?" });
    expect(dialog).toHaveTextContent(
      "Someone else saved this sequence, so your changes since your last save are not saved. Leaving throws them away.",
    );

    fireEvent.click(within(dialog).getByRole("button", { name: "Leave without saving" }));

    expect(await screen.findByText("All the sequences")).toBeInTheDocument();
  });

  it("does not hold up picking another step while something is unsaved", async () => {
    const { saves } = serve(administrator, view(), { step: "p" });

    await opened();
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });
    fireEvent.click(railStep("s"));

    expect(
      await screen.findByRole("heading", { level: 2, name: /Set wallpaper/ }),
    ).toBeInTheDocument();
    expect(saves).toHaveLength(0);
  });

  it("takes another administrator's save live while nothing is unsaved, and says who saved it", async () => {
    const { queryClient } = serve(administrator, view());

    await opened();

    act(() => {
      queryClient.setQueryData(
        ["sequence", sequenceId],
        view({ revision: 4, name: "Lab PCs, room 4", updatedBy: "bob" }),
      );
    });

    await waitFor(() => {
      expect(screen.getByRole("textbox", { name: "Sequence name" })).toHaveValue("Lab PCs, room 4");
    });
    expect(screen.getByText(/^bob saved it at /)).toBeInTheDocument();
  });

  it("removes a step and brings it back where it was", async () => {
    const { saves } = serve(administrator, view(), { step: "i" });

    await opened();
    fireEvent.click(screen.getByRole("button", { name: "Remove Apply image" }));

    expect(order()).toEqual(["p", "s"]);
    // The step that took its place shows.
    expect(railStep("s")).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("heading", { level: 2, name: /Set wallpaper/ })).toBeInTheDocument();
    const undo = screen.getByRole("button", { name: "Undo" });
    await waitFor(() => {
      expect(undo).toHaveFocus();
    });
    expect(
      screen.getByText("Removed Apply image (Apply image). You can bring it back for 10 seconds."),
    ).toBeInTheDocument();

    // A removal is saved at once.
    await waitFor(() => {
      expect(saves.map((save) => save.definition.steps.map((step) => step.id))).toEqual([
        ["p", "s"],
      ]);
    });

    fireEvent.click(undo);

    expect(order()).toEqual(["p", "i", "s"]);
    expect(railStep("i")).toHaveAttribute("aria-selected", "true");
    expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.id)).toEqual(["p", "i", "s"]);
    }, saveWait);
  });

  it("offers to bring a removed step back for 10 s", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    serve(administrator, view(), { step: "i" });

    await opened();
    fireEvent.click(screen.getByRole("button", { name: "Remove Apply image" }));
    expect(screen.getByRole("button", { name: "Undo" })).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(9_000));
    expect(screen.getByRole("button", { name: "Undo" })).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(1_500));
    expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
  });

  it("adds a step of the chosen kind at the end, in the phase of the step before it, and shows it", async () => {
    const { saves } = serve(
      administrator,
      view({ stepPhases: ["WindowsPE", "WindowsPE", "Windows"] }),
    );

    await opened();
    fireEvent.click(screen.getByRole("button", { name: "Add step at the end" }));
    const menu = await screen.findByRole("menu", { name: "Add step at the end" });
    fireEvent.click(within(menu).getByRole("menuitem", { name: "Restart" }));

    await waitFor(() => {
      expect(screen.getByRole("heading", { level: 2, name: /Restart/ })).toBeInTheDocument();
    });
    expect(screen.getByText("Restart, in Windows")).toBeInTheDocument();
    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.kind)).toEqual([
        "partition",
        "applyImage",
        "runScript",
        "reboot",
      ]);
    }, saveWait);
    // The sequence has no description, which the page edits as empty text.
    expect(saves.at(-1)?.description).toBeNull();
  });

  it("inserts a step after the one shown", async () => {
    const { saves } = serve(administrator, view(), { step: "p" });

    await opened();
    fireEvent.click(screen.getByRole("button", { name: "Insert after Partition the disk" }));
    const menu = await screen.findByRole("menu", { name: "Insert after Partition the disk" });
    fireEvent.click(within(menu).getByRole("menuitem", { name: "Run script" }));

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.kind)).toEqual([
        "partition",
        "runScript",
        "applyImage",
        "runScript",
      ]);
    }, saveWait);
    expect(screen.getByRole("heading", { level: 2, name: /^02/ })).toHaveTextContent("Run script");
  });

  it("offers only raw disk images to write, and warns of one not signed for Secure Boot", async () => {
    const { saves } = serve(administrator, linuxView(), {
      images: [image, rawImage, signedRawImage],
      step: "w",
    });

    await opened();
    await loaded("imageId");
    fireEvent.click(within(field("imageId")).getByRole("button"));
    const list = (await screen.findByRole("option", { name: /^noble/ })).closest<HTMLElement>(
      "[role=listbox]",
    );

    if (list === null) {
      throw new Error("The images are not in a list.");
    }

    const options = within(list)
      .getAllByRole("option")
      .map((option) => option.textContent);
    expect(options).toEqual(["nobleNot signed for Secure Boot", "debian-12Signed for Secure Boot"]);
    fireEvent.click(screen.getByRole("option", { name: /^noble/ }));

    expect(
      await screen.findByText(/^This image will not start with Secure Boot on\./),
    ).toBeInTheDocument();
    expect(screen.getByText(/carries no signature\./)).toBeInTheDocument();
    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[0]).toMatchObject({
        kind: "writeRawImage",
        imageId: rawImage.id,
      });
    }, saveWait);

    await choose("imageId", "debian-12");
    expect(screen.queryByText(/will not start with Secure Boot on/)).not.toBeInTheDocument();
  });

  it("starts the seed with the machine's name and a cloud-config, and lists the placeholders", async () => {
    const { saves } = serve(administrator, linuxView(rawImage.id), {
      images: [rawImage],
      step: "c",
    });

    await opened();
    expect(screen.getByRole("textbox", { name: "meta-data" })).toHaveValue(
      'instance-id: "{{SmbiosUuid}}"\nlocal-hostname: "{{ComputerName}}"\n',
    );
    expect(screen.getByRole("textbox", { name: "user-data" })).toHaveValue("#cloud-config\n");
    expect(screen.queryByRole("textbox", { name: "network-config" })).not.toBeInTheDocument();
    expect(screen.getByText(/\{\{SerialNumber\}\}, \{\{SmbiosUuid\}\}/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("checkbox", { name: "Write network-config" }));
    expect(screen.getByRole("textbox", { name: "network-config" })).toHaveValue("version: 2\n");

    // The switch saves at once, and turned off and on again it keeps what was typed.
    await waitFor(
      () => {
        expect(saves.at(-1)?.definition.steps[1]).toMatchObject({ networkConfig: "version: 2\n" });
      },
      { timeout: 500 },
    );
    fireEvent.change(screen.getByRole("textbox", { name: "network-config" }), {
      target: { value: "version: 2\nethernets: {}\n" },
    });
    fireEvent.click(screen.getByRole("checkbox", { name: "Write network-config" }));
    expect(screen.queryByRole("textbox", { name: "network-config" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("checkbox", { name: "Write network-config" }));
    expect(screen.getByRole("textbox", { name: "network-config" })).toHaveValue(
      "version: 2\nethernets: {}\n",
    );
  });

  it("names an image of the other kind instead of calling it deleted", async () => {
    serve(
      administrator,
      view({
        definition: {
          version: 1,
          steps: [{ ...newStep("applyImage", "i"), imageId: rawImage.id } as SequenceStep],
        },
        stepPhases: ["WindowsPE"],
      }),
      { images: [image, rawImage] },
    );

    await opened();
    await waitFor(() => {
      expect(within(field("imageId")).getByRole("button")).toHaveTextContent(
        "noble (raw disk image)",
      );
    });
  });

  it("shows a viewer the sequence without letting anything change", async () => {
    const { saves } = serve(viewer, view(), { step: "s" });

    expect(
      await screen.findByText(
        "Only administrators change task sequences. You can look at this one.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Sequence name" })).toHaveAttribute("readonly");
    expect(screen.getByRole("textbox", { name: "Script" })).toHaveAttribute("readonly");
    // A choice reads as text.
    expect(screen.getByRole("textbox", { name: "Interpreter" })).toHaveValue("cmd");
    expect(screen.queryByRole("button", { name: /^Add step/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Remove/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Move/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add a condition" })).not.toBeInTheDocument();
    expect(screen.queryByText("All changes saved")).not.toBeInTheDocument();

    const first = railStep("s");
    act(() => {
      first.focus();
    });
    fireEvent.keyDown(first, { key: "ArrowLeft", altKey: true });
    expect(order()).toEqual(["p", "i", "s"]);
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(0);
  });

  it("stops saving, unsaved edits too, once someone else deleted the sequence", async () => {
    const { saves, queryClient, remove } = serve(administrator, view());

    await opened();
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });

    // As the live connection does for a sequenceChanged without a revision.
    remove();
    act(() => {
      void queryClient.invalidateQueries({ queryKey: ["sequence", sequenceId] });
    });

    expect(await screen.findByText("This sequence was deleted")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Sequence name" })).toHaveAttribute("readonly");

    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(0);
  });

  it("stops saving once the sequence is gone", async () => {
    const { saves } = serve(administrator, view(), {
      answer: () => new Response(null, { status: 404 }),
    });

    await opened();
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });

    expect(
      await screen.findByText(
        "Not saved: It no longer exists on the server, so nothing more is saved.",
        undefined,
        saveWait,
      ),
    ).toBeInTheDocument();

    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab 2" },
    });
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(1);
  });

  it("says when the sequence does not exist", async () => {
    const { router } = serve(administrator, view());

    await opened();

    await act(() =>
      router.navigate({
        to: "/deployment/sequences/$sequenceId",
        params: { sequenceId: "0193a4b2-0000-7000-8000-0000000000ff" },
      }),
    );

    expect(await screen.findByText("Sequence not found")).toBeInTheDocument();
    expect(
      screen.getByText("This sequence does not exist. It may have been deleted."),
    ).toBeInTheDocument();
  });
});
