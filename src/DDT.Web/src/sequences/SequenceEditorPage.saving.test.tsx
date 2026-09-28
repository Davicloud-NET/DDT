// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { toasts } from "@/ui/toasts";

import {
  administrator,
  json,
  opened,
  saveWait,
  sequenceId,
  serve,
  tab,
  view,
} from "./SequenceEditorPage.fixtures";

describe("SequenceEditorPage", () => {
  afterEach(() => {
    act(() => {
      toasts.clear();
    });
    vi.unstubAllGlobals();
    vi.useRealTimers();
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
    tab("Sequence");
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
    fireEvent.click(within(notice).getByRole("button", { name: "Keep mine" }));
    const dialog = await screen.findByRole("dialog", { name: "Save your version over theirs?" });
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
    tab("Sequence");
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab PCs (mine)" },
    });

    const takeTheirs = await screen.findByRole("button", { name: "Use theirs" }, saveWait);
    await waitFor(() => {
      expect(takeTheirs).toBeEnabled();
    });
    fireEvent.click(takeTheirs);

    expect(screen.getByRole("textbox", { name: "Sequence name" })).toHaveValue("Lab PCs (bob)");
    expect(saves).toHaveLength(1);
    // Their copy came with the refusal.
    expect(reads.slice(readsBefore)).not.toContain(`/api/sequences/${sequenceId}`);
  });

  it("forgets what to undo once it shows another administrator's save", async () => {
    const { saves, queryClient } = serve(administrator, view());

    await opened();
    tab("Sequence");
    fireEvent.change(screen.getByRole("textbox", { name: "Description" }), {
      target: { value: "For room 4" },
    });
    await waitFor(() => {
      expect(saves).toHaveLength(1);
    }, saveWait);
    await screen.findByText(/^All changes saved at /);

    act(() => {
      queryClient.setQueryData(
        ["sequence", sequenceId],
        view({ revision: 9, name: "Lab PCs, room 4", description: "Theirs", updatedBy: "bob" }),
      );
    });
    await waitFor(() => {
      expect(screen.getByRole("textbox", { name: "Description" })).toHaveValue("Theirs");
    });
    expect(screen.getByText(/^bob saved it at /)).toBeInTheDocument();

    fireEvent.keyDown(document.body, { key: "z", ctrlKey: true });
    expect(screen.getByRole("textbox", { name: "Description" })).toHaveValue("Theirs");
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(1);
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
    tab("Sequence");
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

  it("saves at once on leaving, and goes once it is saved", async () => {
    const { saves } = serve(administrator, view());

    await opened();
    tab("Sequence");
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
    tab("Sequence");
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

  it("stops saving, unsaved edits too, once someone else deleted the sequence", async () => {
    const { saves, queryClient, remove } = serve(administrator, view());

    await opened();
    tab("Sequence");
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
  });
});
