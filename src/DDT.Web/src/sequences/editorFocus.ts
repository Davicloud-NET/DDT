// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Moves the focus to a field of the shown step, by the name the server's findings use, such as "imageId" or
// "conditions[0].value". A finding for a whole condition shows at its value. False if no such field is shown.
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

// Alt+Up or Alt+Down moves the step from its fields too, except where those keys work within the field: a text
// area, a list that opens, or a number that steps.
export function movesFrom(target: EventTarget): boolean {
  return !(
    target instanceof HTMLTextAreaElement ||
    (target instanceof HTMLElement &&
      (target.hasAttribute("aria-haspopup") ||
        target.getAttribute("role") === "spinbutton" ||
        target.closest("[role=listbox], [role=menu], [role=dialog]") !== null))
  );
}
