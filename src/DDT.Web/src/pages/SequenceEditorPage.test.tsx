// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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
import {
  SEQUENCE_VERSION,
  type RunScriptStep,
  type SaveSequenceRequest,
  type SequenceProblem,
  type SequenceStep,
  type SequenceView,
} from "@/sequences/sequences";
import { EMPTY_ID, newStep } from "@/sequences/steps";

import { SequenceEditorPage } from "./SequenceEditorPage";

const administrator: CurrentUser = {
  id: "u",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
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

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

type SaveAnswer = (request: SaveSequenceRequest) => Response;

// The server holds one sequence. Saves answer as the given function says; by default they are stored with the
// next revision, as the server does.
function serve(user: CurrentUser, initial: SequenceView, answer?: SaveAnswer) {
  let stored = initial;
  let deleted = false;
  const saves: SaveSequenceRequest[] = [];

  const store: SaveAnswer = (request) => {
    stored = {
      ...stored,
      name: request.name,
      description: request.description,
      definition: request.definition,
      revision: stored.revision + 1,
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
          const response = (answer ?? store)(request);

          // A conflict answers with the copy the server holds, which a read then returns too.
          return response.status !== 409
            ? Promise.resolve(response)
            : response
                .clone()
                .json()
                .then((current: SequenceView) => {
                  stored = current;
                  return response;
                });
        }
        case "GET /api/images":
          return Promise.resolve(json([image]));
        case "GET /api/packages":
          return Promise.resolve(json([]));
        case "GET /api/sequences":
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
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/sequences",
        component: () => <p>All the sequences</p>,
      }),
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/sequences/$sequenceId",
        component: SequenceEditorPage,
      }),
    ]),
    history: createMemoryHistory({ initialEntries: [`/sequences/${sequenceId}`] }),
  });

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  const remove = () => {
    deleted = true;
  };

  return { saves, queryClient, router, stored: () => stored, remove };
}

// The debounce of typing is 700 ms.
const saveWait = { timeout: 3_000 };

function card(name: string): HTMLElement {
  const handle = screen.getByRole("button", { name: `Move ${name}` });
  const item = handle.closest("li");

  if (item === null) {
    throw new Error(`${name} is not in a step card.`);
  }

  return item;
}

const order = () =>
  screen
    .getAllByRole("button", { name: /^Move (?!.* (up|down)$)/ })
    .map((handle) => handle.getAttribute("aria-label"));

describe("SequenceEditorPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("shows each problem on its step and field, and the sequence's own ones above the steps", async () => {
    const problems: SequenceProblem[] = [
      { stepId: "i", field: "imageId", message: "Choose the image to apply." },
      { stepId: "s", field: "conditions[0].value", message: "Enter the value to compare with." },
      { stepId: "s", field: null, message: "A step in Windows needs an earlier step." },
    ];
    const warnings: SequenceProblem[] = [
      { stepId: null, field: null, message: "No Write answer file step adds the administrator." },
    ];

    serve(administrator, view({ problems, warnings }));

    expect(await screen.findByRole("heading", { level: 1, name: "Lab PCs" })).toBeInTheDocument();
    expect(screen.getByText(/^3 problems keep it from running. 1 warning./)).toBeInTheDocument();
    expect(
      screen.getByText("No Write answer file step adds the administrator."),
    ).toBeInTheDocument();

    const imageField = within(card("Apply image")).getByLabelText("Image");
    expect(imageField).toHaveAttribute("aria-invalid", "true");
    expect(imageField).toHaveAccessibleDescription("Choose the image to apply.");
    expect(imageField).toHaveValue(EMPTY_ID);

    const script = card("Set wallpaper");
    expect(within(script).getByLabelText("Value of condition 1")).toHaveAccessibleDescription(
      "Enter the value to compare with.",
    );
    expect(
      within(script).getByText("A step in Windows needs an earlier step."),
    ).toBeInTheDocument();
    // At its field only, not again among the step's own problems.
    expect(within(script).getAllByText("Enter the value to compare with.")).toHaveLength(1);
    expect(within(card("Partition the disk")).queryByRole("list", { name: "Problems" })).toBeNull();
  });

  it("saves an edit in place with the revision it read", async () => {
    const { saves } = serve(administrator, view());

    fireEvent.change(await screen.findByLabelText("Description"), {
      target: { value: "For room 4" },
    });
    fireEvent.change(within(card("Apply image")).getByLabelText("Image"), {
      target: { value: image.id },
    });

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

    fireEvent.change(screen.getByLabelText("Sequence name"), {
      target: { value: "Lab PCs, room 4" },
    });

    await waitFor(() => {
      expect(saves).toHaveLength(2);
    }, saveWait);
    expect(saves[1]).toMatchObject({ revision: 4, name: "Lab PCs, room 4" });
  });

  it("shows the server's refusal of a name at the field", async () => {
    serve(administrator, view(), () =>
      json(
        { title: "Invalid", errors: { name: ["Another sequence is already called Lab."] } },
        400,
      ),
    );

    fireEvent.change(await screen.findByLabelText("Sequence name"), { target: { value: "Lab" } });

    await waitFor(() => {
      expect(screen.getByLabelText("Sequence name")).toHaveAccessibleDescription(
        "Another sequence is already called Lab.",
      );
    }, saveWait);
    expect(
      screen.getByText("Not saved: Another sequence is already called Lab."),
    ).toBeInTheDocument();
  });

  it("keeps numbers the server cannot store out of the document", async () => {
    const { saves } = serve(administrator, view());

    await screen.findByRole("heading", { level: 1, name: "Lab PCs" });
    const script = within(card("Set wallpaper"));
    fireEvent.change(script.getByLabelText("Timeout (minutes)"), {
      target: { value: "3000000000" },
    });
    fireEvent.change(script.getByLabelText("Exit codes that mean success"), {
      target: { value: "0, 3000000000" },
    });

    expect(
      script.getByText(
        "Enter a whole number from -2147483648 to 2147483647. Until then the last number stays.",
      ),
    ).toBeInTheDocument();
    expect(
      script.getByText(
        "Enter whole numbers from -2147483648 to 2147483647, separated by commas. Until then the last list stays.",
      ),
    ).toBeInTheDocument();

    fireEvent.change(script.getByLabelText("Timeout (minutes)"), { target: { value: "90" } });

    await waitFor(() => {
      expect(saves).toHaveLength(1);
    }, saveWait);
    expect(saves[0]?.definition.steps[2]).toMatchObject({
      timeoutMinutes: 90,
      successExitCodes: [0],
    });
  });

  it("moves a step with the keyboard, keeps the focus on it, says where it went and saves the order", async () => {
    const { saves } = serve(administrator, view());

    const handle = await screen.findByRole("button", { name: "Move Apply image" });
    handle.focus();
    fireEvent.keyDown(handle, { key: "ArrowUp" });

    expect(order()).toEqual(["Move Apply image", "Move Partition the disk", "Move Set wallpaper"]);
    expect(screen.getByRole("button", { name: "Move Apply image" })).toHaveFocus();
    expect(screen.getByText("Apply image moved to position 1 of 3.")).toBeInTheDocument();

    const name = within(card("Set wallpaper")).getByLabelText("Name");
    name.focus();
    fireEvent.keyDown(name, { key: "ArrowUp", altKey: true });

    expect(order()).toEqual(["Move Apply image", "Move Set wallpaper", "Move Partition the disk"]);
    expect(within(card("Set wallpaper")).getByLabelText("Name")).toHaveFocus();

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.id)).toEqual(["i", "s", "p"]);
    }, saveWait);
  });

  it("moves a step to the first or the last place with Home and End on its Move button", async () => {
    serve(administrator, view());

    const handle = await screen.findByRole("button", { name: "Move Set wallpaper" });
    handle.focus();
    fireEvent.keyDown(handle, { key: "Home" });

    expect(order()).toEqual(["Move Set wallpaper", "Move Partition the disk", "Move Apply image"]);

    fireEvent.keyDown(screen.getByRole("button", { name: "Move Set wallpaper" }), { key: "End" });

    expect(order()).toEqual(["Move Partition the disk", "Move Apply image", "Move Set wallpaper"]);
  });

  it("names who saved in between, and keeps this page's version only once confirmed", async () => {
    const theirs = view({ revision: 4, name: "Lab PCs (bob)", updatedBy: "bob" });
    let conflicted = false;
    const { saves } = serve(administrator, view(), (request) => {
      if (!conflicted) {
        conflicted = true;
        return json(theirs, 409);
      }

      return json({ ...theirs, name: request.name, revision: 5 });
    });

    fireEvent.change(await screen.findByLabelText("Sequence name"), {
      target: { value: "Lab PCs (mine)" },
    });

    const banner = await screen.findByText(
      /^bob saved this sequence at .* while you were editing\. Your changes since your last save are not saved\.$/,
      undefined,
      saveWait,
    );
    const alert = banner.closest("section");

    if (alert === null) {
      throw new Error("The conflict is not in its banner.");
    }

    expect(within(alert).getByText("They changed the name.")).toBeInTheDocument();
    expect(screen.getByText("Not saved: someone else saved this sequence")).toBeInTheDocument();

    fireEvent.click(await within(alert).findByRole("button", { name: "Keep mine" }));
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

  it("takes the other version and drops this page's edits", async () => {
    const theirs = view({ revision: 4, name: "Lab PCs (bob)", updatedBy: "bob" });
    const { saves } = serve(administrator, view(), () => json(theirs, 409));

    fireEvent.change(await screen.findByLabelText("Sequence name"), {
      target: { value: "Lab PCs (mine)" },
    });

    // Enabled once their copy is read.
    const takeTheirs = await screen.findByRole("button", { name: "Use theirs" }, saveWait);
    await waitFor(() => {
      expect(takeTheirs).toBeEnabled();
    });
    fireEvent.click(takeTheirs);

    expect(screen.getByLabelText("Sequence name")).toHaveValue("Lab PCs (bob)");
    expect(screen.queryByRole("button", { name: "Use theirs" })).not.toBeInTheDocument();
    expect(saves).toHaveLength(1);
  });

  it("saves at once on leaving, and goes once it is saved", async () => {
    const { saves } = serve(administrator, view());

    fireEvent.change(await screen.findByLabelText("Sequence name"), { target: { value: "Lab" } });
    fireEvent.click(screen.getByRole("link", { name: "All sequences" }));

    expect(await screen.findByText("All the sequences")).toBeInTheDocument();
    expect(saves).toEqual([expect.objectContaining({ name: "Lab", revision: 3 })]);
  });

  it("asks before leaving when the changes cannot be saved", async () => {
    serve(administrator, view(), () => json(view({ revision: 4, updatedBy: "bob" }), 409));

    fireEvent.change(await screen.findByLabelText("Sequence name"), { target: { value: "Lab" } });
    fireEvent.click(screen.getByRole("link", { name: "All sequences" }));

    const dialog = await screen.findByRole("dialog", { name: "Leave without saving?" });
    expect(dialog).toHaveTextContent(
      "Someone else saved this sequence, so your changes since your last save are not saved. Leaving throws them away.",
    );

    fireEvent.click(within(dialog).getByRole("button", { name: "Leave without saving" }));

    expect(await screen.findByText("All the sequences")).toBeInTheDocument();
  });

  it("takes another administrator's save live while nothing is unsaved", async () => {
    const { queryClient } = serve(administrator, view());

    await screen.findByRole("heading", { level: 1, name: "Lab PCs" });

    act(() => {
      queryClient.setQueryData(
        ["sequence", sequenceId],
        view({ revision: 4, name: "Lab PCs, room 4", updatedBy: "bob" }),
      );
    });

    await waitFor(() => {
      expect(screen.getByLabelText("Sequence name")).toHaveValue("Lab PCs, room 4");
    });
    expect(screen.getByText("All changes saved")).toBeInTheDocument();
  });

  it("removes a step and brings it back where it was", async () => {
    const { saves } = serve(administrator, view());

    fireEvent.click(await screen.findByRole("button", { name: "Remove Apply image" }));

    expect(screen.queryByRole("button", { name: "Move Apply image" })).not.toBeInTheDocument();
    const undo = screen.getByRole("button", { name: "Undo" });
    expect(undo).toHaveFocus();
    expect(screen.getByText(/Removed Apply image: Apply image\./)).toBeInTheDocument();

    // A removal is saved at once.
    await waitFor(() => {
      expect(saves.map((save) => save.definition.steps.map((step) => step.id))).toEqual([
        ["p", "s"],
      ]);
    });

    fireEvent.click(undo);

    expect(order()).toEqual(["Move Partition the disk", "Move Apply image", "Move Set wallpaper"]);
    expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.id)).toEqual(["p", "i", "s"]);
    }, saveWait);
  });

  it("divides the steps by the phase the server gives, and puts a new step in the phase before it", async () => {
    serve(administrator, view({ stepPhases: ["WindowsPE", "WindowsPE", "Windows"] }));

    const stepsUnder = (divider: string) => {
      const group = screen.getByRole("heading", { level: 2, name: divider }).closest("section");

      if (group === null) {
        throw new Error(`${divider} divides no steps.`);
      }

      return within(group)
        .getAllByRole("button", { name: /^Move (?!.* (up|down)$)/ })
        .map((handle) => handle.getAttribute("aria-label"));
    };

    await screen.findByRole("heading", { level: 1, name: "Lab PCs" });
    expect(stepsUnder("Windows PE")).toEqual(["Move Partition the disk", "Move Apply image"]);
    expect(stepsUnder("Windows, after the hand-over")).toEqual(["Move Set wallpaper"]);

    fireEvent.change(screen.getByLabelText("Kind of step to add at the end"), {
      target: { value: "reboot" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add step" }));

    expect(stepsUnder("Windows, after the hand-over")).toEqual([
      "Move Set wallpaper",
      "Move Restart",
    ]);
  });

  it("adds a step of the chosen kind at the end", async () => {
    const { saves } = serve(administrator, view());

    fireEvent.change(await screen.findByLabelText("Kind of step to add at the end"), {
      target: { value: "reboot" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add step" }));

    expect(screen.getByRole("button", { name: "Move Restart" })).toBeInTheDocument();
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

  it("offers to bring a removed step back for 10 s", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    serve(administrator, view());

    fireEvent.click(await screen.findByRole("button", { name: "Remove Apply image" }));
    expect(screen.getByRole("button", { name: "Undo" })).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(9_000));
    expect(screen.getByRole("button", { name: "Undo" })).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(1_500));
    expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
  });

  it("shows a viewer the sequence without letting anything change", async () => {
    const { saves } = serve(viewer, view());

    expect(
      await screen.findByText("Read only: only administrators change sequences."),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Sequence name")).toBeDisabled();
    expect(within(card("Set wallpaper")).getByLabelText("Script")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Add step" })).toBeDisabled();
    expect(saves).toHaveLength(0);
  });

  it("does not move a step for a viewer from a link in its card", async () => {
    const withLink = [
      newStep("partition", "p"),
      newStep("injectDrivers", "d"),
      newStep("reboot", "r"),
    ];
    const { saves } = serve(viewer, view({ definition: { version: 1, steps: withLink } }));

    const link = await screen.findByRole("link", {
      name: "Driver packages and their targets are on the Packages page.",
    });
    link.focus();
    fireEvent.keyDown(link, { key: "ArrowDown", altKey: true });

    expect(order()).toEqual(["Move Partition the disk", "Move Inject drivers", "Move Restart"]);
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(0);
  });

  it("stops saving, unsaved edits too, once someone else deleted the sequence", async () => {
    const { saves, queryClient, remove } = serve(administrator, view());

    fireEvent.change(await screen.findByLabelText("Sequence name"), { target: { value: "Lab" } });

    // As the live connection does for a sequenceChanged without a revision.
    remove();
    act(() => {
      void queryClient.invalidateQueries({ queryKey: ["sequence", sequenceId] });
    });

    expect(
      await screen.findByText("This sequence was deleted, so nothing more is saved."),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Sequence name")).toBeDisabled();

    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(0);
  });

  it("stops saving once the sequence is gone", async () => {
    const { saves } = serve(administrator, view(), () => new Response(null, { status: 404 }));

    fireEvent.change(await screen.findByLabelText("Sequence name"), { target: { value: "Lab" } });

    expect(
      await screen.findByText(
        "Not saved: It no longer exists on the server, so nothing more is saved.",
        undefined,
        saveWait,
      ),
    ).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Sequence name"), { target: { value: "Lab 2" } });
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(1);
  });
});
