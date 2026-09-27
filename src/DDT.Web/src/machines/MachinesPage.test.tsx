// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { MachineSummary } from "@/machines/machines";
import type { MachineSequenceResolution } from "@/rules/rules";
import { sequencesQuery } from "@/sequences/sequences";
import {
  chooseMenuItem,
  chooseOption,
  fill,
  menuItems,
  press,
  selectKey,
  selectOptions,
} from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import {
  deploymentOptions,
  deploymentSummary,
  machineSummary,
  operator,
  sequenceResolution,
  sequenceSummary,
  viewer,
} from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import type { Routes } from "@/test/server";

const now = new Date("2026-09-16T10:10:00Z");

function secondsBefore(seconds: number): string {
  return new Date(now.getTime() - seconds * 1000).toISOString();
}

const installWindowsId = "0193a4b2-0000-7000-8000-0000000000e1";
const installLinuxId = "0193a4b2-0000-7000-8000-0000000000e2";
const labPcsId = "0193a4b2-0000-7000-8000-0000000000e3";

const installWindows = sequenceSummary({ id: installWindowsId });
const labPcs = sequenceSummary({ id: labPcsId, name: "Lab PCs" });
const linux = sequenceSummary({
  id: installLinuxId,
  name: "Install Linux",
  stepCount: 2,
  rawImageName: "noble",
  rawImageBootCapability: "NotSigned",
});

// A machine that registered and waits, as every machine starts.
function waiting(overrides: Partial<MachineSummary> = {}): MachineSummary {
  return machineSummary({
    state: "Pending",
    everApproved: false,
    firstSeenUtc: "2026-09-16T10:00:00Z",
    lastSeenUtc: "2026-09-16T10:00:00Z",
    ...overrides,
  });
}

function modelRule(overrides: Partial<MachineSequenceResolution> = {}): MachineSequenceResolution {
  return sequenceResolution({
    source: "ModelRule",
    sequenceId: installWindowsId,
    sequenceName: "Install Windows",
    ruleId: "0193a4b2-0000-7000-8000-0000000000f1",
    explanation:
      "The rule for model Virtual Machine chooses Install Windows. A rule only chooses: the machine still needs an approval on the web, or someone who signs in at it, where the sequence is offered.",
    ...overrides,
  });
}

const label = "Virtual Machine (00:15:5D:01:02:03)";

function open(
  machines: MachineSummary[],
  routes: Routes = {},
  options: { user?: typeof operator; live?: boolean; narrow?: boolean; path?: string } = {},
): Promise<RenderedPage> {
  return renderPage({
    path: options.path ?? "/machines",
    user: options.user ?? operator,
    routes: { "GET /api/machines": { body: machines }, ...routes },
    ...(options.live === undefined ? {} : { live: options.live }),
    ...(options.narrow === undefined ? {} : { narrow: options.narrow }),
  });
}

// The row of the machine with this name; the name is the link to its page.
function row(name: string): HTMLElement {
  const found = screen.getByRole("link", { name }).closest("tr, li");

  if (!(found instanceof HTMLElement)) {
    throw new Error(`${name} is not in a row.`);
  }

  return found;
}

function moreFor(name: string): HTMLElement {
  return screen.getByRole("button", { name: `More for ${name}` });
}

// The answers the assign dialog reads, plus what the test adds.
function assigning(extra: Routes = {}): Routes {
  return {
    "GET /api/sequences": { body: [installWindows] },
    "GET /api/deployments/options": { body: deploymentOptions({ serverUtc: now.toISOString() }) },
    ...extra,
  };
}

// Opens the assign dialog of the one machine on the page, from its key or, for a waiting machine, its menu.
async function openAssign(viaMenu = false): Promise<HTMLElement> {
  await screen.findByRole("link", { name: "Virtual Machine" });

  if (viaMenu) {
    await chooseMenuItem(moreFor(label), "Assign a sequence");
  } else {
    press(within(row("Virtual Machine")).getByRole("button", { name: "Assign" }));
  }

  return screen.findByRole("dialog", { name: `Assign a task sequence to ${label}` });
}

function assignKey(dialog: HTMLElement): HTMLElement {
  return within(dialog).getByRole("button", { name: "Assign sequence" });
}

async function assignable(dialog: HTMLElement): Promise<void> {
  await waitFor(() => {
    expect(assignKey(dialog)).toBeEnabled();
  });
}

function fact(scope: HTMLElement, name: string): string | null {
  return within(scope).getByText(name, { selector: "dt" }).nextElementSibling?.textContent ?? null;
}

describe("MachinesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("the list", () => {
    it("explains that no machine has registered yet", async () => {
      await open([]);

      expect(
        await screen.findByRole("heading", { level: 1, name: "All machines" }),
      ).toBeInTheDocument();
      expect(await screen.findByText("No machines yet")).toBeInTheDocument();
      expect(
        screen.getByText(/^Machines appear here on their own when they netboot/),
      ).toBeInTheDocument();
    });

    it("lists a machine with its name, hardware, state and who signed in at it", async () => {
      await open([waiting({ signedInBy: "bob" })]);

      const link = await screen.findByRole("link", { name: "Virtual Machine" });
      expect(link).toHaveAttribute("href", "/machines/0193a4b2-0000-7000-8000-000000000001");

      const machine = within(row("Virtual Machine"));
      expect(machine.getByText("Microsoft Corporation, serial 1234")).toBeInTheDocument();
      expect(machine.getByText("Waiting")).toBeInTheDocument();
      expect(machine.getByText("bob signed in at the machine")).toBeInTheDocument();
      expect(machine.getByText("Needs your approval")).toBeInTheDocument();
    });

    it("shows the machine picked in the list beside it, with its MAC and Secure Boot", async () => {
      const { router } = await open([
        machineSummary({ id: "m1", assignedName: "PC-ON", secureBootEnabled: true }),
        machineSummary({
          id: "m2",
          assignedName: "PC-OFF",
          primaryMac: "00155D0A0B0C",
          macAddresses: ["00155D0A0B0C"],
          secureBootEnabled: false,
        }),
      ]);

      press(within(await screen.findByRole("row", { name: /^PC-ON/ })).getByText("Ready"));

      await waitFor(() => {
        expect(router.state.location.search).toEqual({ selected: "m1" });
      });
      const on = screen.getByRole("complementary", { name: "PC-ON" });
      expect(fact(on, "MAC address")).toBe("00:15:5D:01:02:03");
      expect(fact(on, "Secure Boot")).toBe("On");
      expect(within(on).getByRole("link", { name: "Open machine" })).toHaveAttribute(
        "href",
        "/machines/m1",
      );

      press(within(row("PC-OFF")).getByText("Ready"));

      const off = await screen.findByRole("complementary", { name: "PC-OFF" });
      expect(fact(off, "MAC address")).toBe("00:15:5D:0A:0B:0C");
      expect(fact(off, "Secure Boot")).toBe("Off");

      press(within(off).getByRole("button", { name: "Close the details" }));

      await waitFor(() => {
        expect(screen.queryByRole("complementary")).not.toBeInTheDocument();
      });
      expect(router.state.location.search).toEqual({});
    });

    it("filters by state and finds a machine by its MAC with or without separators", async () => {
      const { router } = await open([
        waiting({ id: "m1", assignedName: "PC-WAITING" }),
        machineSummary({
          id: "m2",
          assignedName: "PC-READY",
          primaryMac: "00155D0A0B0C",
          macAddresses: ["00155D0A0B0C"],
        }),
      ]);

      await screen.findByRole("link", { name: "PC-READY" });
      const filters = screen.getByRole("radiogroup", { name: "Show machines by state" });
      press(within(filters).getByRole("radio", { name: /^Waiting/ }));

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-READY" })).not.toBeInTheDocument();
      });
      expect(screen.getByRole("link", { name: "PC-WAITING" })).toBeInTheDocument();
      expect(router.state.location.search).toEqual({ state: "waiting" });

      press(within(filters).getByRole("radio", { name: /^All/ }));
      fill(screen.getByRole("searchbox", { name: "Find a machine" }), "00-15-5d-0a-0b-0c");

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-WAITING" })).not.toBeInTheDocument();
      });
      expect(screen.getByRole("link", { name: "PC-READY" })).toBeInTheDocument();

      fill(screen.getByRole("searchbox", { name: "Find a machine" }), "PC-NONE");

      expect(await screen.findByText("No machine matches")).toBeInTheDocument();
      press(screen.getByRole("button", { name: "Show all machines" }));

      expect(await screen.findByRole("link", { name: "PC-WAITING" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "PC-READY" })).toBeInTheDocument();
    });

    it("lists machines as rows of their own on a phone", async () => {
      await open([waiting({ assignedName: "PC-042" })], {}, { narrow: true });

      const list = await screen.findByRole("list", { name: "Machines" });
      expect(within(list).getByRole("link", { name: "PC-042" })).toBeInTheDocument();
      expect(within(list).getByText("Waiting")).toBeInTheDocument();
      expect(within(list).getByRole("button", { name: "Approve" })).toBeInTheDocument();
      expect(screen.queryByRole("grid")).not.toBeInTheDocument();
    });

    it("shows each machine's run: the step and its percent, why no step runs, and where it failed", async () => {
      vi.useFakeTimers({ toFake: ["Date"], now });

      await open(
        [
          machineSummary({
            id: "m1",
            assignedName: "PC-RUNNING",
            state: "Deploying",
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Running",
              stepIndex: 3,
              stepName: "Apply image",
              percent: 45,
              phase: "WindowsPE",
              activity: "Step",
              startedUtc: secondsBefore(125),
            }),
          }),
          machineSummary({
            id: "m7",
            assignedName: "PC-RESTARTING",
            state: "Deploying",
            lastSeenUtc: secondsBefore(60),
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Running",
              stepIndex: 4,
              stepName: "Restart",
              phase: "WindowsPE",
              activity: "Restarting",
              startedUtc: secondsBefore(600),
            }),
          }),
          machineSummary({
            id: "m8",
            assignedName: "PC-SETUP",
            state: "Deploying",
            lastSeenUtc: secondsBefore(720),
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Running",
              stepIndex: 5,
              stepName: "Write the answer file",
              phase: "Windows",
              activity: "WaitingForWindowsSetup",
              startedUtc: secondsBefore(1800),
            }),
          }),
          // The check before the start failed, so the disk was never touched and no step is recorded.
          machineSummary({
            id: "m2",
            assignedName: "PC-FAILED",
            state: "Failed",
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Failed",
              error: "The disk is smaller than the image needs.",
              finishedUtc: secondsBefore(500),
            }),
          }),
          machineSummary({
            id: "m6",
            assignedName: "PC-STOPPED",
            state: "Failed",
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Failed",
              stepIndex: 4,
              stepName: "Install agent",
              percent: 40,
              error: "Stopped by operator.",
              startedUtc: secondsBefore(900),
              finishedUtc: secondsBefore(800),
            }),
          }),
          machineSummary({
            id: "m3",
            assignedName: "PC-DONE",
            state: "Done",
            deployment: deploymentSummary({
              stepCount: 9,
              state: "Done",
              stepIndex: 8,
              stepName: "Restart",
              percent: 100,
              startedUtc: secondsBefore(1500),
              finishedUtc: secondsBefore(300),
            }),
          }),
          machineSummary({
            id: "m4",
            assignedName: "PC-CANCELLED",
            deployment: deploymentSummary({ state: "Cancelled" }),
          }),
          machineSummary({
            id: "m9",
            assignedName: "PC-RULE",
            deployment: deploymentSummary({ state: "Assigned", source: "Rule" }),
          }),
          machineSummary({ id: "m5", assignedName: "PC-NONE" }),
        ],
        {},
        { path: "/machines?selected=m1" },
      );

      const running = within(await screen.findByRole("row", { name: /^PC-RUNNING/ }));
      expect(running.getByText("Install Windows")).toBeInTheDocument();
      expect(running.getByText("Apply image 45%")).toBeInTheDocument();
      expect(
        running.getByRole("img", { name: "Step 4 of 9 running, 45 percent" }),
      ).toBeInTheDocument();
      expect(running.queryByText(/last contact/)).not.toBeInTheDocument();

      const panel = within(screen.getByRole("complementary", { name: "PC-RUNNING" }));
      expect(panel.getByText("Step 4 of 9")).toBeInTheDocument();
      expect(panel.getByText("Running for 2 min 5 s")).toBeInTheDocument();

      expect(
        within(row("PC-RESTARTING")).getByText("Restarting, last contact 1 minute ago"),
      ).toBeInTheDocument();
      expect(
        within(row("PC-SETUP")).getByText("Waiting for Windows setup, last contact 12 minutes ago"),
      ).toBeInTheDocument();

      // No step failed where none ran.
      const failed = within(row("PC-FAILED"));
      expect(failed.getAllByText("Failed")).toHaveLength(2);
      expect(failed.queryByText(/Step \d+ failed/)).not.toBeInTheDocument();
      expect(failed.getByRole("img", { name: "Not started, 9 steps" })).toBeInTheDocument();

      const stopped = within(row("PC-STOPPED"));
      expect(stopped.getByText("Step 5 failed")).toBeInTheDocument();
      expect(stopped.getByRole("img", { name: "Failed at step 5 of 9" })).toBeInTheDocument();

      expect(
        within(row("PC-DONE")).getByText("Took 20 min 0 s, finished 5 minutes ago"),
      ).toBeInTheDocument();
      expect(within(row("PC-CANCELLED")).getByText("Stopped")).toBeInTheDocument();
      expect(
        within(row("PC-RULE")).getByText("Approved by operator with the sequence a rule chose"),
      ).toBeInTheDocument();
      expect(within(row("PC-NONE")).getByText("No sequence assigned")).toBeInTheDocument();
    });

    it("puts the machines that need someone first", async () => {
      await open([
        machineSummary({ id: "m1", assignedName: "PC-DONE", state: "Done" }),
        waiting({ id: "m2", assignedName: "PC-WAITING" }),
        machineSummary({ id: "m3", assignedName: "PC-FAILED", state: "Failed" }),
      ]);

      await screen.findByRole("link", { name: "PC-DONE" });
      expect(
        screen
          .getAllByRole("row")
          .slice(1)
          .map((each) => within(each).getByRole("link").textContent),
      ).toEqual(["PC-WAITING", "PC-FAILED", "PC-DONE"]);
    });
  });

  describe("strays", () => {
    it("offers to remove machines nobody approved, and all of them from one address once", async () => {
      await open([
        waiting({ id: "m1", assignedName: "PC-1", firstSeenAddress: "10.0.0.9" }),
        waiting({ id: "m2", assignedName: "PC-2", firstSeenAddress: "10.0.0.9" }),
        waiting({ id: "m3", assignedName: "PC-3", firstSeenAddress: "10.0.0.5" }),
        waiting({
          id: "m4",
          assignedName: "PC-4",
          firstSeenAddress: "10.0.0.9",
          everApproved: true,
        }),
      ]);

      await screen.findByRole("link", { name: "PC-1" });
      const offers: [string, boolean, string[]][] = [];

      for (const name of ["PC-1", "PC-2", "PC-3", "PC-4"]) {
        const items = await menuItems(moreFor(name));

        offers.push([
          name,
          items.includes("Remove"),
          items.filter((item) => item.startsWith("Remove all")),
        ]);
      }

      // The first of the strays from an address offers to remove them all.
      expect(offers).toEqual([
        ["PC-1", true, ["Remove all 2 waiting from 10.0.0.9"]],
        ["PC-2", true, []],
        ["PC-3", true, []],
        ["PC-4", false, []],
      ]);
    });

    it("removes every stray from one address without reading the list again", async () => {
      const { server } = await open(
        [
          waiting({ id: "m1", assignedName: "PC-1", firstSeenAddress: "10.0.0.9" }),
          waiting({ id: "m2", assignedName: "PC-2", firstSeenAddress: "10.0.0.9" }),
          waiting({ id: "m3", assignedName: "PC-3", firstSeenAddress: "10.0.0.5" }),
        ],
        { "DELETE /api/machines?waitingFrom=10.0.0.9": { status: 204 } },
      );

      await screen.findByRole("link", { name: "PC-1" });
      await chooseMenuItem(moreFor("PC-1"), "Remove all 2 waiting from 10.0.0.9");

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-1" })).not.toBeInTheDocument();
      });
      expect(screen.queryByRole("link", { name: "PC-2" })).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "PC-3" })).toBeInTheDocument();
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("offers to remove a rejected machine, but does not count it with the strays", async () => {
      const { server } = await open(
        [
          waiting({ id: "m1", assignedName: "PC-WAITING", firstSeenAddress: "10.0.0.7" }),
          machineSummary({
            id: "m2",
            assignedName: "PC-REJECTED",
            state: "Rejected",
            firstSeenAddress: "10.0.0.7",
          }),
        ],
        { "DELETE /api/machines/m2": { status: 204 } },
      );

      await screen.findByRole("link", { name: "PC-REJECTED" });
      expect(await menuItems(moreFor("PC-WAITING"))).not.toContainEqual(
        expect.stringMatching(/^Remove all/),
      );

      await chooseMenuItem(moreFor("PC-REJECTED"), "Remove");

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-REJECTED" })).not.toBeInTheDocument();
      });
      expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
        "DELETE /api/machines/m2",
      ]);
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("does not offer to remove a waiting machine with an assigned sequence, nor count it with the strays", async () => {
      await open([
        waiting({ id: "m1", assignedName: "PC-1", firstSeenAddress: "10.0.0.5" }),
        waiting({ id: "m2", assignedName: "PC-2", firstSeenAddress: "10.0.0.5" }),
        waiting({
          id: "m3",
          assignedName: "PC-ASSIGNED",
          firstSeenAddress: "10.0.0.5",
          deployment: deploymentSummary({ state: "Assigned" }),
        }),
      ]);

      await screen.findByRole("link", { name: "PC-ASSIGNED" });
      expect(await menuItems(moreFor("PC-ASSIGNED"))).not.toContainEqual(
        expect.stringMatching(/^Remove/),
      );

      const offers = [
        ...(await menuItems(moreFor("PC-1"))),
        ...(await menuItems(moreFor("PC-2"))),
      ].filter((item) => item.startsWith("Remove all"));
      expect(offers).toEqual(["Remove all 2 waiting from 10.0.0.5"]);
    });
  });

  describe("assigning a sequence", () => {
    it("tells before assigning that a machine waiting at the prompt gets authorized, then assigns", async () => {
      vi.useFakeTimers({ toFake: ["Date"], now });

      const machine = waiting({ lastSeenUtc: secondsBefore(30) });
      const { server } = await open(
        [machine],
        assigning({
          "GET /api/sequences": {
            body: [
              sequenceSummary({ continuesInWindows: true }),
              sequenceSummary({ id: labPcsId, name: "Lab PCs", problemCount: 2 }),
            ],
          },
          [`GET /api/machines/${machine.id}/sequence`]: { body: sequenceResolution() },
          [`POST /api/machines/${machine.id}/deployments`]: {
            body: { ...machine, state: "Approved", deployment: deploymentSummary() },
          },
        }),
      );

      const dialog = await openAssign(true);

      expect(
        await within(dialog).findByText(
          "Install Windows erases all data on the disk of Virtual Machine (00:15:5D:01:02:03).",
        ),
      ).toBeInTheDocument();
      expect(
        within(dialog).getByText(
          /^After the image is applied, the run goes on in the installed Windows/,
        ),
      ).toBeInTheDocument();
      expect(
        within(dialog).getByText(
          "Model: Virtual Machine. Reported disks: Disk 0: Msft Virtual Disk, 64 GB, SCSI.",
        ),
      ).toBeInTheDocument();
      expect(
        within(dialog).getByText(
          "Last seen from 172.25.132.98 30 seconds ago; nobody has signed in at it.",
        ),
      ).toBeInTheDocument();
      expect(
        await within(dialog).findByText(
          "This also authorizes the machine, which then runs the sequence and receives the deployment passwords.",
        ),
      ).toBeInTheDocument();
      expect(within(dialog).queryByText(/stays waiting/)).not.toBeInTheDocument();

      // A sequence with problems cannot run, so it cannot be chosen.
      expect(await selectOptions(dialog, "Task sequence")).toEqual([
        { text: "Install Windows", disabled: false },
        { text: "Lab PCs2 problems, cannot run", disabled: true },
      ]);

      fill(within(dialog).getByLabelText("Computer name"), "PC-042");
      await assignable(dialog);
      const listReads = server.count("GET /api/machines");
      press(assignKey(dialog));

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(server.changes().map((request) => [request.path, request.body])).toEqual([
        [
          `/api/machines/${machine.id}/deployments`,
          { sequenceId: installWindowsId, computerName: "PC-042" },
        ],
      ]);
      // The answer is the machine as the server stored it, so the list is not read again.
      expect(await screen.findByText("Assigned by operator")).toBeInTheDocument();
      expect(within(row("Virtual Machine")).getByText("Ready")).toBeInTheDocument();
      expect(server.count("GET /api/machines")).toBe(listReads);
    });

    it("preselects the sequence a rule chose and says so", async () => {
      const machine = machineSummary();
      const { server } = await open(
        [machine],
        assigning({
          "GET /api/sequences": { body: [installWindows, labPcs] },
          [`GET /api/machines/${machine.id}/sequence`]: {
            body: modelRule({
              sequenceId: labPcsId,
              sequenceName: "Lab PCs",
              explanation: "The rule for model Virtual Machine chooses Lab PCs.",
            }),
          },
          [`POST /api/machines/${machine.id}/deployments`]: {
            body: { ...machine, deployment: deploymentSummary({ title: "Lab PCs" }) },
          },
        }),
      );

      const dialog = await openAssign();

      expect(
        await within(dialog).findByText("The rule for model Virtual Machine chooses Lab PCs."),
      ).toBeInTheDocument();
      expect(selectKey(dialog, "Task sequence")).toHaveTextContent("Lab PCs");

      await assignable(dialog);
      press(assignKey(dialog));

      await waitFor(() => {
        expect(server.changes().map((request) => request.body)).toEqual([
          { sequenceId: labPcsId, computerName: null },
        ]);
      });
    });

    it("explains that no sequence can be assigned while none exists", async () => {
      await open([machineSummary()], assigning({ "GET /api/sequences": { body: [] } }));

      const dialog = await openAssign();

      expect(
        await within(dialog).findByText(
          "No task sequence exists yet. An administrator creates one under Deployment, Task sequences.",
        ),
      ).toBeInTheDocument();
      expect(assignKey(dialog)).toBeDisabled();
    });

    it("explains that no sequence can be assigned while every one has problems", async () => {
      await open(
        [machineSummary()],
        assigning({ "GET /api/sequences": { body: [sequenceSummary({ problemCount: 1 })] } }),
      );

      const dialog = await openAssign();

      expect(
        await within(dialog).findByText(/^Every task sequence has problems, so none can run\./),
      ).toBeInTheDocument();
      expect(assignKey(dialog)).toBeDisabled();
    });

    it.each([
      {
        waiting: "not seen for a while, with zero touch networks set",
        seconds: 600,
        settings: { zeroTouchEnabled: true },
        lastSeen: "Last seen from 172.25.132.98 10 minutes ago; nobody has signed in at it.",
        consequence:
          "It stays waiting until someone signs in at it or it netboots from a zero touch network.",
      },
      {
        waiting: "not seen for a while, without zero touch networks",
        seconds: 600,
        settings: {},
        lastSeen: "Last seen from 172.25.132.98 10 minutes ago; nobody has signed in at it.",
        consequence: "It stays waiting until someone signs in at it.",
      },
      {
        waiting: "at the prompt, when the server requires web approval",
        seconds: 30,
        settings: { requireWebApproval: true, zeroTouchEnabled: true },
        lastSeen: "Last seen from 172.25.132.98 30 seconds ago; nobody has signed in at it.",
        consequence: "It stays waiting until someone signs in at it.",
      },
    ])(
      "says that a machine $waiting stays waiting after the assignment",
      async ({ seconds, settings, lastSeen, consequence }) => {
        vi.useFakeTimers({ toFake: ["Date"], now });

        await open(
          [waiting({ lastSeenUtc: secondsBefore(seconds) })],
          assigning({
            "GET /api/deployments/options": {
              body: deploymentOptions({ serverUtc: now.toISOString(), ...settings }),
            },
          }),
        );

        const dialog = await openAssign(true);

        expect(within(dialog).getByText(lastSeen)).toBeInTheDocument();
        expect(await within(dialog).findByText(consequence)).toBeInTheDocument();
        expect(within(dialog).queryByText(/This also authorizes/)).not.toBeInTheDocument();
        expect(within(dialog).getAllByText(/stays waiting/)).toHaveLength(1);
      },
    );

    it("says that under web approval the assignment authorizes a machine someone signed in at", async () => {
      vi.useFakeTimers({ toFake: ["Date"], now });

      // The server does not look at the last contact in this case.
      await open(
        [waiting({ lastSeenUtc: secondsBefore(600), signedInBy: "tech" })],
        assigning({
          "GET /api/deployments/options": {
            body: deploymentOptions({ serverUtc: now.toISOString(), requireWebApproval: true }),
          },
        }),
      );

      const dialog = await openAssign(true);

      expect(
        within(dialog).getByText("Last seen from 172.25.132.98 10 minutes ago; signed in by tech."),
      ).toBeInTheDocument();
      expect(
        await within(dialog).findByText(
          "This also authorizes the machine, because tech signed in at it. It then runs the sequence and receives the deployment passwords.",
        ),
      ).toBeInTheDocument();
      expect(within(dialog).queryByText(/stays waiting/)).not.toBeInTheDocument();
    });

    it("judges a machine waiting at the prompt by the server's clock when the browser's is ahead", async () => {
      // The browser's clock is two minutes ahead of the server's.
      vi.useFakeTimers({ toFake: ["Date"], now: now.getTime() + 120_000 });

      await open([waiting({ lastSeenUtc: secondsBefore(30) })], assigning());

      const dialog = await openAssign(true);

      expect(
        await within(dialog).findByText(
          "This also authorizes the machine, which then runs the sequence and receives the deployment passwords.",
        ),
      ).toBeInTheDocument();
      expect(
        within(dialog).getByText(
          "Last seen from 172.25.132.98 30 seconds ago; nobody has signed in at it.",
        ),
      ).toBeInTheDocument();
      expect(within(dialog).queryByText(/stays waiting/)).not.toBeInTheDocument();
    });

    it("says only once that a machine reported no disk to install on", async () => {
      await open([machineSummary({ disks: null, eligibleDiskCount: 0 })], assigning());

      const dialog = await openAssign();

      expect(within(dialog).getByText("Model: Virtual Machine.")).toBeInTheDocument();
      expect(
        await within(dialog).findByText(
          "The machine reported no disk DDT can install on, so the run will fail.",
        ),
      ).toBeInTheDocument();
      expect(within(dialog).queryByText(/has not reported its disks/)).not.toBeInTheDocument();
    });

    it("names a sequence's warnings, and a missing disk only for a sequence that erases one", async () => {
      await open(
        [machineSummary({ disks: null, eligibleDiskCount: 0 })],
        assigning({
          "GET /api/sequences": { body: [sequenceSummary({ erasesDisk: false, warningCount: 1 })] },
        }),
      );

      const dialog = await openAssign();

      expect(
        await within(dialog).findByText(
          "Install Windows has 1 warning. It runs, but look at the sequence first.",
        ),
      ).toBeInTheDocument();
      expect(within(dialog).queryByText(/reported no disk/)).not.toBeInTheDocument();
    });

    it("does not offer the assignment when the deployment settings cannot be loaded", async () => {
      vi.useFakeTimers({ toFake: ["Date"], now });

      await open(
        [waiting({ lastSeenUtc: secondsBefore(30) })],
        assigning({
          "GET /api/deployments/options": { status: 500, body: { title: "Internal error." } },
        }),
      );

      const dialog = await openAssign(true);

      expect(
        await within(dialog).findByText(
          "The deployment settings could not be loaded, so DDT cannot say what the assignment does. Close this and try again.",
        ),
      ).toBeInTheDocument();
      expect(selectKey(dialog, "Task sequence")).toHaveTextContent("Install Windows");
      expect(assignKey(dialog)).toBeDisabled();
      expect(
        within(dialog).queryByText(/This also authorizes|stays waiting/),
      ).not.toBeInTheDocument();
    });

    it("requires a computer name when the sequence joins the domain and the machine has none", async () => {
      const { server } = await open(
        [machineSummary()],
        assigning({
          "GET /api/sequences": { body: [sequenceSummary({ needsComputerName: true })] },
          "GET /api/deployments/options": { body: deploymentOptions({ domainConfigured: true }) },
        }),
      );

      const dialog = await openAssign();
      const name = within(dialog).getByLabelText("Computer name");

      await waitFor(() => {
        expect(name).toHaveAccessibleDescription(
          /^Required, because Install Windows joins the domain under this name\./,
        );
      });
      expect(name).toBeRequired();
      await assignable(dialog);

      press(assignKey(dialog));

      await waitFor(() => {
        expect(name).toHaveAttribute("aria-invalid", "true");
      });
      expect(name).toHaveAccessibleDescription(
        /Enter a computer name\. Install Windows joins the domain under this name\./,
      );
      expect(server.changes()).toEqual([]);
    });

    it("asks for no computer name when a domain is configured but the sequence does not join it", async () => {
      await open(
        [machineSummary()],
        assigning({
          "GET /api/deployments/options": { body: deploymentOptions({ domainConfigured: true }) },
        }),
      );

      const dialog = await openAssign();
      const name = within(dialog).getByLabelText("Computer name");

      await waitFor(() => {
        expect(name).toHaveAccessibleDescription(/^Optional\. Without a name, Windows picks one\./);
      });
      expect(name).not.toBeRequired();
    });

    it("shows why the server refused an assignment", async () => {
      const machine = machineSummary();
      await open(
        [machine],
        assigning({
          [`POST /api/machines/${machine.id}/deployments`]: {
            status: 409,
            body: {
              title: "This machine already has a run. Cancel it before assigning another sequence.",
            },
          },
        }),
      );

      const dialog = await openAssign();
      await assignable(dialog);
      press(assignKey(dialog));

      expect(await within(dialog).findByRole("alert")).toHaveTextContent(
        "This machine already has a run. Cancel it before assigning another sequence.",
      );
    });

    it("shows the server's refusal of a computer name at the field", async () => {
      const machine = machineSummary();
      await open(
        [machine],
        assigning({
          [`POST /api/machines/${machine.id}/deployments`]: {
            status: 400,
            body: {
              title: "One or more validation errors occurred.",
              errors: { computerName: ["A computer name has at most 15 characters."] },
            },
          },
        }),
      );

      const dialog = await openAssign();
      const name = within(dialog).getByLabelText("Computer name");
      await assignable(dialog);
      fill(name, "PC_042");
      press(assignKey(dialog));

      await waitFor(() => {
        expect(name).toHaveAttribute("aria-invalid", "true");
      });
      expect(name).toHaveAccessibleDescription(/A computer name has at most 15 characters\./);
      // Said once, at the field.
      expect(within(dialog).queryByRole("alert")).not.toBeInTheDocument();
    });

    it("does not assign a sequence that erases a disk to a machine with more than one", async () => {
      await open([machineSummary({ eligibleDiskCount: 2 })], assigning());

      const dialog = await openAssign();

      expect(
        await within(dialog).findByText(
          "This machine has more than one disk. Sign in at it and choose the disk there.",
        ),
      ).toBeInTheDocument();
      expect(assignKey(dialog)).toBeDisabled();
    });

    it("assigns a sequence that erases no disk to a machine with more than one", async () => {
      await open(
        [machineSummary({ eligibleDiskCount: 2 })],
        assigning({ "GET /api/sequences": { body: [sequenceSummary({ erasesDisk: false })] } }),
      );

      const dialog = await openAssign();

      expect(
        await within(dialog).findByText(
          "Install Windows does not erase the disk of Virtual Machine (00:15:5D:01:02:03).",
        ),
      ).toBeInTheDocument();
      expect(within(dialog).queryByText(/more than one disk/)).not.toBeInTheDocument();
      await assignable(dialog);
    });

    it("assigns a raw disk image not signed for Secure Boot to a machine with it on only once allowed", async () => {
      const machine = machineSummary({ secureBootEnabled: true });
      const { server, queryClient } = await open(
        [machine],
        assigning({
          "GET /api/sequences": { body: [installWindows, linux] },
          [`POST /api/machines/${machine.id}/deployments`]: {
            body: {
              ...machine,
              deployment: deploymentSummary({ sequenceId: installLinuxId, title: "Install Linux" }),
            },
          },
        }),
      );

      const dialog = await openAssign();
      await assignable(dialog);

      // A Windows sequence starts with Secure Boot on: nothing to allow.
      expect(within(dialog).queryByRole("checkbox")).not.toBeInTheDocument();

      await chooseOption(dialog, "Task sequence", "Install Linux");

      const warning = /^noble is not signed for Secure Boot, and this machine has Secure Boot on\./;
      expect(within(dialog).getByText(warning)).toBeInTheDocument();
      expect(assignKey(dialog)).toBeDisabled();

      const allow = within(dialog).getByRole("checkbox", { name: "Write noble anyway" });
      expect(allow).toHaveAccessibleDescription(warning);
      press(allow);
      expect(assignKey(dialog)).toBeEnabled();

      // Another sequence asks again.
      await chooseOption(dialog, "Task sequence", "Install Windows");
      await chooseOption(dialog, "Task sequence", "Install Linux");
      expect(
        within(dialog).getByRole("checkbox", { name: "Write noble anyway" }),
      ).not.toBeChecked();

      // So does the same sequence once a live update makes it write another image.
      press(within(dialog).getByRole("checkbox", { name: "Write noble anyway" }));
      act(() => {
        queryClient.setQueryData(sequencesQuery.queryKey, [
          installWindows,
          { ...linux, rawImageName: "jammy" },
        ]);
      });
      const jammy = await within(dialog).findByRole("checkbox", { name: "Write jammy anyway" });
      expect(jammy).not.toBeChecked();
      expect(assignKey(dialog)).toBeDisabled();

      press(jammy);
      press(assignKey(dialog));

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(server.changes().map((request) => request.body)).toEqual([
        { sequenceId: installLinuxId, computerName: null, allowSecureBootMismatch: true },
      ]);
    });

    it("assigns a raw disk image where the machine did not say whether Secure Boot is on", async () => {
      const machine = machineSummary();
      const { server } = await open(
        [machine],
        assigning({
          "GET /api/sequences": {
            body: [{ ...linux, needsComputerName: true, rawImageBootCapability: "Unknown" }],
          },
          [`POST /api/machines/${machine.id}/deployments`]: { body: machine },
        }),
      );

      const dialog = await openAssign();

      expect(
        await within(dialog).findByText(
          "noble may not start with Secure Boot on, as DDT could not tell whether it is signed for it. The machine has not said whether Secure Boot is on. If it is, the run stops before it erases anything, unless you allow the image here.",
        ),
      ).toBeInTheDocument();
      const name = within(dialog).getByLabelText("Computer name");
      expect(name).toHaveAccessibleDescription(
        /^Required, because Install Linux gives this name to the machine in its cloud-init seed\./,
      );

      fill(name, "LAB-01");
      await assignable(dialog);
      press(assignKey(dialog));

      await waitFor(() => {
        expect(server.changes().map((request) => request.body)).toEqual([
          { sequenceId: installLinuxId, computerName: "LAB-01" },
        ]);
      });
    });
  });

  describe("approving", () => {
    it("approves a waiting machine at once when no rule chooses its sequence", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: sequenceResolution() },
        [`POST /api/machines/${machine.id}/approve`]: {
          body: { ...machine, state: "Approved", everApproved: true },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));

      expect(await within(row("Virtual Machine")).findByText("Ready")).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(server.changes().map((request) => [request.path, request.body])).toEqual([
        [`/api/machines/${machine.id}/approve`, null],
      ]);
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("confirms an approval that runs the sequence a rule chose, and names that sequence to the server", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
        [`POST /api/machines/${machine.id}/approve`]: {
          body: {
            ...machine,
            state: "Approved",
            everApproved: true,
            deployment: deploymentSummary({ source: "Rule" }),
          },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      const dialog = await screen.findByRole("dialog", { name: `Approve ${label}?` });

      expect(
        within(dialog).getByText(
          "Approving Virtual Machine (00:15:5D:01:02:03) also runs Install Windows on it, which a rule for its model chose. All data on its disk is erased.",
        ),
      ).toBeInTheDocument();
      expect(server.changes()).toEqual([]);

      press(within(dialog).getByRole("button", { name: "Approve and run Install Windows" }));

      expect(
        await screen.findByText("Approved by operator with the sequence a rule chose"),
      ).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(server.changes().map((request) => request.body)).toEqual([
        { expectedSequenceId: installWindowsId },
      ]);
    });

    it("approves with a rule's raw disk image not signed for Secure Boot only once allowed", async () => {
      const machine = waiting({ secureBootEnabled: true });
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: {
          body: modelRule({ sequenceId: installLinuxId, sequenceName: "Install Linux" }),
        },
        "GET /api/sequences": { body: [linux] },
        [`POST /api/machines/${machine.id}/approve`]: {
          body: { ...machine, state: "Approved", everApproved: true },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      const dialog = await screen.findByRole("dialog", { name: `Approve ${label}?` });
      const confirm = within(dialog).getByRole("button", { name: "Approve and run Install Linux" });

      // The warning is part of what the dialog says as it opens.
      expect(dialog).toHaveAccessibleDescription(
        /which a rule for its model chose\. All data on its disk is erased\. noble is not signed for Secure Boot, and this machine has Secure Boot on\./,
      );
      expect(confirm).toBeDisabled();

      press(within(dialog).getByRole("checkbox", { name: "Write noble anyway" }));
      press(confirm);

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(server.changes().map((request) => request.body)).toEqual([
        { expectedSequenceId: installLinuxId, allowSecureBootMismatch: true },
      ]);
    });

    it("reads what the rules choose afresh before an approval, however recently it was read", async () => {
      const machine = waiting();
      const { server, queryClient } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
      });

      // As the application keeps reads, with copies that no longer hold.
      queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } });
      queryClient.setQueryData(["machine-sequence", machine.id], sequenceResolution());
      queryClient.setQueryData(["sequences"], [sequenceSummary({ erasesDisk: false })]);

      press(await screen.findByRole("button", { name: "Approve" }));

      const dialog = await screen.findByRole("dialog");
      expect(
        within(dialog).getByText(
          "Approving Virtual Machine (00:15:5D:01:02:03) also runs Install Windows on it, which a rule for its model chose. All data on its disk is erased.",
        ),
      ).toBeInTheDocument();
      expect(server.changes()).toEqual([]);
    });

    it("closes the approval confirmation once someone else decided", async () => {
      const machine = waiting();
      const { hub } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      await screen.findByRole("dialog");

      act(() => {
        hub?.push("machineChanged", { ...machine, state: "Rejected" });
      });

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
    });

    it("shows in the confirmation why the rules no longer choose that sequence", async () => {
      const machine = waiting();
      await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
        [`POST /api/machines/${machine.id}/approve`]: {
          status: 409,
          body: { title: "The rules no longer choose that sequence for this machine." },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      const dialog = await screen.findByRole("dialog");
      press(within(dialog).getByRole("button", { name: "Approve and run Install Windows" }));

      expect(await within(dialog).findByRole("alert")).toHaveTextContent(
        "The rules no longer choose that sequence for this machine.",
      );
    });

    it("approves without running a rule's sequence that has problems, after saying so", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule({ problemCount: 2 }) },
        "GET /api/sequences": { body: [sequenceSummary({ problemCount: 2 })] },
        [`POST /api/machines/${machine.id}/approve`]: {
          body: { ...machine, state: "Approved", everApproved: true },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      const dialog = await screen.findByRole("dialog");

      expect(
        within(dialog).getByText(
          "A rule for its model chooses Install Windows, but it has 2 problems and cannot run until they are fixed. Approving authorizes Virtual Machine (00:15:5D:01:02:03) without running anything.",
        ),
      ).toBeInTheDocument();

      press(within(dialog).getByRole("button", { name: "Approve without a sequence" }));

      await waitFor(() => {
        expect(server.changes().map((request) => [request.path, request.body])).toEqual([
          [`/api/machines/${machine.id}/approve`, null],
        ]);
      });
    });

    it("does not approve when what the rules choose cannot be read", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: {
          status: 500,
          body: { title: "The server failed." },
        },
      });

      press(await screen.findByRole("button", { name: "Approve" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("The server failed.");
      expect(server.changes()).toEqual([]);
      expect(within(row("Virtual Machine")).getByText("Waiting")).toBeInTheDocument();
    });

    it("rejects a machine from its menu", async () => {
      const machine = waiting();
      const { server } = await open([machine], {
        [`POST /api/machines/${machine.id}/reject`]: { body: { ...machine, state: "Rejected" } },
      });

      await chooseMenuItem(
        await screen.findByRole("button", { name: `More for ${label}` }),
        "Reject",
      );

      expect(await within(row("Virtual Machine")).findByText("Rejected")).toBeInTheDocument();
      expect(server.changes().map((request) => request.path)).toEqual([
        `/api/machines/${machine.id}/reject`,
      ]);
    });
  });

  describe("stopping and cancelling", () => {
    const deploying = machineSummary({
      state: "Deploying",
      deployment: deploymentSummary({
        state: "Running",
        stepIndex: 1,
        stepName: "Apply image",
        percent: 10,
      }),
    });

    it("stops a running deployment after saying what that leaves behind", async () => {
      const { server } = await open([deploying], {
        [`DELETE /api/machines/${deploying.id}/deployments/current`]: {
          body: {
            ...deploying,
            state: "Failed",
            deployment: deploymentSummary({
              state: "Failed",
              stepIndex: 1,
              stepName: "Apply image",
              error: "Stopped by operator.",
            }),
          },
        },
      });

      await chooseMenuItem(
        await screen.findByRole("button", { name: `More for ${label}` }),
        "Stop the run",
      );
      const dialog = await screen.findByRole("dialog", { name: `Stop the run on ${label}?` });

      expect(dialog).toHaveAccessibleDescription(
        "This stops the run on Virtual Machine (00:15:5D:01:02:03). Its disk is left half written; assign a sequence again to deploy it.",
      );

      press(within(dialog).getByRole("button", { name: "Stop the run" }));

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(within(row("Virtual Machine")).getByText("Step 2 failed")).toBeInTheDocument();
      expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
        `DELETE /api/machines/${deploying.id}/deployments/current`,
      ]);
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("closes the stop confirmation when the deployment it was opened for ends", async () => {
      const { server, hub } = await open([deploying]);

      await chooseMenuItem(
        await screen.findByRole("button", { name: `More for ${label}` }),
        "Stop the run",
      );
      await screen.findByRole("dialog", { name: `Stop the run on ${label}?` });

      // Meanwhile the agent reported that the same run failed.
      act(() => {
        hub?.push("machineChanged", {
          ...deploying,
          state: "Failed",
          deployment: deploymentSummary({
            state: "Failed",
            stepIndex: 1,
            stepName: "Apply image",
            error: "The image could not be applied.",
          }),
        });
      });

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(within(row("Virtual Machine")).getByText("Step 2 failed")).toBeInTheDocument();
      expect(await menuItems(moreFor(label))).not.toContain("Stop the run");
      expect(server.changes()).toEqual([]);
    });

    it("closes the stop confirmation when another deployment replaced the one it was opened for", async () => {
      const { server, hub } = await open([deploying]);

      await chooseMenuItem(
        await screen.findByRole("button", { name: `More for ${label}` }),
        "Stop the run",
      );
      await screen.findByRole("dialog", { name: `Stop the run on ${label}?` });

      // Meanwhile the run failed, and a technician picked another sequence at the machine.
      act(() => {
        hub?.push("machineChanged", {
          ...deploying,
          deployment: deploymentSummary({
            id: "0193a4b2-0000-7000-8000-0000000000d2",
            state: "Running",
            stepIndex: 0,
            stepName: "Partition",
            source: "Console",
            requestedBy: "tech",
          }),
        });
      });

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(server.changes()).toEqual([]);

      // The new deployment can be stopped after its own confirmation.
      await chooseMenuItem(moreFor(label), "Stop the run");
      expect(
        await screen.findByRole("dialog", { name: `Stop the run on ${label}?` }),
      ).toBeInTheDocument();
    });

    it("cancels an assigned deployment", async () => {
      const assigned = machineSummary({ deployment: deploymentSummary({ state: "Assigned" }) });
      const { server } = await open([assigned], {
        [`DELETE /api/machines/${assigned.id}/deployments/current`]: {
          body: {
            ...assigned,
            deployment: deploymentSummary({
              state: "Cancelled",
              finishedUtc: new Date().toISOString(),
            }),
          },
        },
      });

      expect(await screen.findByText("Assigned by operator")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Assign" })).not.toBeInTheDocument();

      await chooseMenuItem(moreFor(label), "Cancel the assignment");

      expect(await within(row("Virtual Machine")).findByText(/^Stopped/)).toBeInTheDocument();
      expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
        `DELETE /api/machines/${assigned.id}/deployments/current`,
      ]);
      expect(
        within(row("Virtual Machine")).getByRole("button", { name: "Assign" }),
      ).toBeInTheDocument();
    });

    it("offers no actions to viewers", async () => {
      await open(
        [deploying, waiting({ id: "m2", assignedName: "PC-WAITING" })],
        {},
        { user: viewer },
      );

      expect(await screen.findByRole("link", { name: "PC-WAITING" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /^More for/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Assign" })).not.toBeInTheDocument();
    });
  });

  describe("live updates", () => {
    it("patches a machine the hub pushes into its row, without reading the list again", async () => {
      const { server, hub } = await open([waiting({ assignedName: "PC-042" })]);

      await screen.findByRole("link", { name: "PC-042" });

      act(() => {
        hub?.push("machineChanged", machineSummary({ assignedName: "PC-042", signedInBy: "bob" }));
        hub?.push("machineChanged", machineSummary({ id: "m2", assignedName: "PC-NEW" }));
      });

      expect(await screen.findByRole("link", { name: "PC-NEW" })).toBeInTheDocument();
      expect(within(row("PC-042")).getByText("Ready")).toBeInTheDocument();
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("flashes a machine whose state the hub changed, and lets one that appears enter", async () => {
      const { hub } = await open([
        waiting({ assignedName: "PC-042" }),
        waiting({ id: "m3", assignedName: "PC-QUIET" }),
      ]);

      await screen.findByRole("link", { name: "PC-042" });
      expect(row("PC-042").className).not.toMatch(/live-/);

      act(() => {
        hub?.push("machineChanged", machineSummary({ assignedName: "PC-042" }));
        hub?.push("machineChanged", machineSummary({ id: "m2", assignedName: "PC-NEW" }));
        hub?.push(
          "machineChanged",
          waiting({ id: "m3", assignedName: "PC-QUIET", signedInBy: "bob" }),
        );
      });

      await screen.findByRole("link", { name: "PC-NEW" });
      // Ready, so it flashes in the quiet tone of that state.
      expect(row("PC-042")).toHaveClass("live-flash", "live-tone-idle");
      expect(row("PC-NEW")).toHaveClass("live-new");
      // Someone signed in at it, but it still waits: its state did not change.
      expect(row("PC-QUIET").className).not.toMatch(/live-/);
    });

    it("marks nothing it read again after a reconnect", async () => {
      const { server, hub } = await open([waiting({ assignedName: "PC-042" })]);

      await screen.findByRole("link", { name: "PC-042" });

      server.routes["GET /api/machines"] = {
        body: [
          machineSummary({ assignedName: "PC-042", state: "Failed" }),
          machineSummary({ id: "m2", assignedName: "PC-NEW" }),
        ],
      };
      act(() => {
        hub?.loseConnection();
        hub?.reconnect();
      });

      await screen.findByRole("link", { name: "PC-NEW" });
      expect(within(row("PC-042")).getByText("Failed")).toBeInTheDocument();
      expect(row("PC-042").className).not.toMatch(/live-/);
      expect(row("PC-NEW").className).not.toMatch(/live-/);
    });

    it("drops the machines the hub says were removed", async () => {
      const { server, hub } = await open(
        [
          waiting({ id: "m1", assignedName: "PC-1" }),
          waiting({ id: "m2", assignedName: "PC-2" }),
          waiting({ id: "m3", assignedName: "PC-3" }),
        ],
        {},
        { path: "/machines?selected=m2" },
      );

      await screen.findByRole("complementary", { name: "PC-2" });

      act(() => {
        hub?.push("machinesRemoved", { machineIds: ["m1", "m2"] });
      });

      await waitFor(() => {
        expect(screen.queryByRole("link", { name: "PC-1" })).not.toBeInTheDocument();
      });
      expect(screen.queryByRole("link", { name: "PC-2" })).not.toBeInTheDocument();
      expect(screen.queryByRole("complementary")).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "PC-3" })).toBeInTheDocument();
      expect(server.count("GET /api/machines")).toBe(1);
    });

    it("reads the list every 5 s only while the live connection is down", async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      const { server, hub } = await open([machineSummary()]);
      const reads = () => server.count("GET /api/machines");

      await screen.findByRole("link", { name: "Virtual Machine" });
      await act(() => vi.advanceTimersByTimeAsync(15_000));
      expect(reads()).toBe(1);

      act(() => {
        hub?.loseConnection();
      });
      await act(() => vi.advanceTimersByTimeAsync(5_000));
      expect(reads()).toBe(2);
      await act(() => vi.advanceTimersByTimeAsync(5_000));
      expect(reads()).toBe(3);

      // Back up, the list is read once for what the hub sent meanwhile, and then only pushed.
      await act(async () => {
        hub?.reconnect();
        await vi.advanceTimersByTimeAsync(15_000);
      });
      expect(reads()).toBe(4);
    });

    it("reads the list every 5 s without a live connection", async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      const { server } = await open([machineSummary()], {}, { live: false });

      await screen.findByRole("link", { name: "Virtual Machine" });
      const first = server.count("GET /api/machines");
      await act(() => vi.advanceTimersByTimeAsync(10_000));

      expect(server.count("GET /api/machines")).toBe(first + 2);
    });
  });

  describe("accessibility", () => {
    it("has no violations in a list with a machine picked", async () => {
      await open(
        [
          waiting({ id: "m1", assignedName: "PC-WAITING", signedInBy: "bob" }),
          machineSummary({
            id: "m2",
            assignedName: "PC-RUNNING",
            state: "Deploying",
            deployment: deploymentSummary({
              state: "Running",
              stepIndex: 1,
              stepName: "Apply image",
              percent: 45,
              startedUtc: "2026-09-16T10:00:00Z",
            }),
          }),
        ],
        {},
        { path: "/machines?selected=m2" },
      );

      await screen.findByRole("complementary", { name: "PC-RUNNING" });
      await expectNoAxeViolations();
    });

    it("has no violations while no machine has registered", async () => {
      await open([]);

      await screen.findByText("No machines yet");
      await expectNoAxeViolations();
    });

    it("has no violations on a phone", async () => {
      await open([waiting({ assignedName: "PC-042" })], {}, { narrow: true });

      await screen.findByRole("list", { name: "Machines" });
      await expectNoAxeViolations();
    });

    it("has no violations with the assign dialog open", async () => {
      await open(
        [machineSummary({ secureBootEnabled: true })],
        assigning({
          "GET /api/sequences": { body: [linux] },
        }),
      );

      const dialog = await openAssign();
      await within(dialog).findByRole("checkbox", { name: "Write noble anyway" });
      await expectNoAxeViolations();
    });

    it("has no violations with the approval confirmation open", async () => {
      const machine = waiting();
      await open([machine], {
        [`GET /api/machines/${machine.id}/sequence`]: { body: modelRule() },
        "GET /api/sequences": { body: [installWindows] },
      });

      press(await screen.findByRole("button", { name: "Approve" }));
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });

    it("has no violations with a machine's menu open", async () => {
      await open([waiting()]);

      press(await screen.findByRole("button", { name: `More for ${label}` }));
      await screen.findByRole("menu");
      await expectNoAxeViolations();
    });
  });
});
