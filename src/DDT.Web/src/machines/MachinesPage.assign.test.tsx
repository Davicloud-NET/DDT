// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { sequencesQuery } from "@/sequences/sequences";
import { chooseOption, fill, press, selectKey, selectOptions } from "@/test/aria";
import {
  deploymentOptions,
  deploymentSummary,
  machineSummary,
  sequenceResolution,
  sequenceSummary,
} from "@/test/builders";

import {
  assignable,
  assigning,
  assignKey,
  installLinuxId,
  installWindows,
  installWindowsId,
  labPcs,
  labPcsId,
  linux,
  modelRule,
  now,
  open,
  openAssign,
  row,
  secondsBefore,
  waiting,
} from "./MachinesPage.fixtures";

describe("MachinesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
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

    // A rule's name pattern gives the machine a name, and the server uses it. A name typed here would win over it.
    it("takes the computer name the machine's values give when none is typed", async () => {
      const machine = machineSummary();
      const { server } = await open(
        [machine],
        assigning({
          "GET /api/sequences": { body: [sequenceSummary({ needsComputerName: true })] },
          "GET /api/deployments/options": { body: deploymentOptions({ domainConfigured: true }) },
          [`GET /api/machines/${machine.id}/sequence`]: {
            body: sequenceResolution({
              values: [
                {
                  name: "ComputerName",
                  value: "PC-00042",
                  source: "Rule",
                  sourceId: "0193a4b2-0000-7000-8000-0000000000f1",
                  sourceName: "Office PCs",
                  overridden: false,
                },
              ],
            }),
          },
          [`POST /api/machines/${machine.id}/deployments`]: {
            body: { ...machine, state: "Approved" },
          },
        }),
      );

      const dialog = await openAssign();
      const name = within(dialog).getByLabelText("Computer name");

      await waitFor(() => {
        expect(name).toHaveAccessibleDescription(
          /^Optional\. Left empty, the machine is named PC-00042, as its values say, and joins the domain under it\./,
        );
      });
      expect(name).not.toBeRequired();
      await assignable(dialog);

      press(assignKey(dialog));

      await waitFor(() => {
        expect(server.changes().map((request) => request.body)).toEqual([
          { sequenceId: installWindowsId, computerName: null },
        ]);
      });
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

      // A Windows sequence boots with Secure Boot on, so there's nothing to allow.
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
});
