// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import { fill, press } from "@/test/aria";

import type { AnswerDraft, AskedInput } from "../inputs";
import { MultiChoiceField } from "./MultiChoiceField";

const names = [
  "7-Zip",
  "Firefox",
  "Chrome",
  "VLC",
  "Notepad++",
  "Teams",
  "Zoom",
  "Office",
  "Acrobat",
];

function input(count: number): AskedInput {
  return {
    name: "Apps",
    label: "Applications",
    help: null,
    kind: "MultiChoice",
    choices: names
      .slice(0, count)
      .map((name, index) => ({ value: `a${String(index)}`, label: name })),
    default: null,
    required: false,
    maxLength: null,
    account: null,
  };
}

function Harness({ asked, start }: { asked: AskedInput; start: string[] }) {
  const [draft, setDraft] = useState<AnswerDraft>({
    value: "",
    values: start,
    userName: "",
    password: "",
  });

  return (
    <>
      <MultiChoiceField input={asked} draft={draft} hint={null} error={null} onChange={setDraft} />
      <output>{draft.values.join(";")}</output>
    </>
  );
}

function renderField(count: number, start: string[] = []) {
  render(
    <I18nProvider i18n={i18n}>
      <Harness asked={input(count)} start={start} />
    </I18nProvider>,
  );
}

describe("a multi-choice question", () => {
  it("shows a list of eight choices as it always did, without a search", () => {
    renderField(8, ["a1"]);

    expect(screen.getAllByRole("checkbox")).toHaveLength(8);
    expect(screen.queryByRole("searchbox")).not.toBeInTheDocument();
    expect(screen.queryByText("1 chosen")).not.toBeInTheDocument();
  });

  it("searches a longer list by label and counts what is chosen", () => {
    renderField(9, ["a1"]);

    expect(screen.getByText("1 chosen")).toBeInTheDocument();
    fill(screen.getByRole("searchbox", { name: "Search Applications" }), "FIRE");

    expect(screen.getAllByRole("checkbox").map((box) => box.closest("label")?.textContent)).toEqual(
      ["Firefox"],
    );
  });

  it("keeps a choice ticked while the search hides it", () => {
    renderField(9, ["a1"]);

    fill(screen.getByRole("searchbox", { name: "Search Applications" }), "zo");
    press(screen.getByRole("checkbox", { name: "Zoom" }));

    expect(screen.queryByRole("checkbox", { name: "Firefox" })).not.toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("a1;a6");
    expect(screen.getByText("2 chosen")).toBeInTheDocument();
  });

  it("says when nothing matches", () => {
    renderField(9);

    fill(screen.getByRole("searchbox", { name: "Search Applications" }), "Photoshop");

    expect(screen.queryAllByRole("checkbox")).toEqual([]);
    expect(screen.getByText("No choice matches.")).toBeInTheDocument();
  });
});
