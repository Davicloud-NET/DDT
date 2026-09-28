// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Takes the focus to a field of the step shown, by the name the server's findings give it, such as "imageId" or
// "conditions[0].value"; a whole condition's finding shows at its value. False when no such field shows.
export function focusField(container: HTMLElement | null, field: string | null): boolean {
  if (field === null || container === null) {
    return false;
  }

  const slot =
    container.querySelector(`[data-field="${CSS.escape(field)}"]`) ??
    container.querySelector(`[data-field="${CSS.escape(`${field}.value`)}"]`);
  const target = slot?.querySelector<HTMLElement>("input, textarea, button") ?? null;

  target?.focus();

  return target !== null;
}

// Alt with Up or Down moves the step from its fields too, except where those keys work within the field: a text
// area, a list that opens, a number that steps.
export function movesFrom(target: EventTarget): boolean {
  return !(
    target instanceof HTMLTextAreaElement ||
    (target instanceof HTMLElement &&
      (target.hasAttribute("aria-haspopup") ||
        target.getAttribute("role") === "spinbutton" ||
        target.closest("[role=listbox], [role=menu], [role=dialog]") !== null))
  );
}
