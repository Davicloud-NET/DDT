// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { expect } from "vitest";

// React Aria's controls open popovers and menus of their own; these work them as a person would, through their
// keys and options.

// A click with a mouse, as a browser sends it. React Aria takes a bare click event for a screen reader's, which
// moves the focus as a keyboard would; in the machine list that also selects the first row the focus enters.
export function press(element: Element): void {
  const pointer = { pointerId: 1, pointerType: "mouse", isPrimary: true, width: 1, height: 1 };

  fireEvent.pointerDown(element, { ...pointer, button: 0, buttons: 1, pressure: 0.5 });
  fireEvent.mouseDown(element, { button: 0, buttons: 1, detail: 1 });
  fireEvent.pointerUp(element, { ...pointer, button: 0, buttons: 0, pressure: 0 });
  fireEvent.mouseUp(element, { button: 0, buttons: 0, detail: 1 });
  fireEvent.click(element, { button: 0, buttons: 0, detail: 1 });
}

// Opens the menu behind a key and gives back the names of its items, then closes it again.
export async function menuItems(key: HTMLElement): Promise<string[]> {
  press(key);
  const menu = await screen.findByRole("menu");
  const items = within(menu)
    .getAllByRole("menuitem")
    .map((item) => item.textContent);

  fireEvent.keyDown(menu, { key: "Escape" });
  await waitFor(() => {
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  });

  return items;
}

export async function chooseMenuItem(key: HTMLElement, item: string): Promise<void> {
  press(key);
  const menu = await screen.findByRole("menu");

  press(within(menu).getByRole("menuitem", { name: item }));
  await waitFor(() => {
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  });
}

// The key of a Select, named by its value and its label.
export function selectKey(scope: HTMLElement, label: string): HTMLElement {
  return within(scope).getByRole("button", { name: new RegExp(`${label}$`) });
}

// The options a Select offers, with whether each can be chosen, and closes it again.
export async function selectOptions(
  scope: HTMLElement,
  label: string,
): Promise<{ text: string; disabled: boolean }[]> {
  press(selectKey(scope, label));
  const listbox = await screen.findByRole("listbox");
  const options = within(listbox)
    .getAllByRole("option")
    .map((option) => ({
      text: option.textContent,
      disabled: option.getAttribute("aria-disabled") === "true",
    }));

  fireEvent.keyDown(listbox, { key: "Escape" });
  await waitFor(() => {
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  return options;
}

export async function chooseOption(
  scope: HTMLElement,
  label: string,
  option: string,
): Promise<void> {
  press(selectKey(scope, label));
  const listbox = await screen.findByRole("listbox");

  press(within(listbox).getByRole("option", { name: option }));
  await waitFor(() => {
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });
}

// Replaces what a field holds, as typing it would.
export function fill(field: HTMLElement, value: string): void {
  fireEvent.change(field, { target: { value } });
}

// The item at an index, which the test knows is there.
export function nth<T>(items: readonly T[], index: number): T {
  const item = items[index];

  if (item === undefined) {
    throw new Error(`There is no item ${String(index)}.`);
  }

  return item;
}
