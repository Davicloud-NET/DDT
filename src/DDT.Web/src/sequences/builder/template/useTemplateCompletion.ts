// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId, useState, type KeyboardEvent, type RefObject } from "react";

import { complete, completionAt } from "../../flow/templates";
import { offeredNames } from "./offeredNames";

// Completion in a template field. Typing {{ offers the names, Up and Down choose one, and Enter or Tab inserts it.
export function useTemplateCompletion({
  input,
  value,
  onChange,
  names,
  locked,
}: {
  // The field's input, for the caret.
  input: RefObject<HTMLInputElement | null>;
  value: string;
  onChange: (value: string) => void;
  names: readonly string[];
  locked: boolean;
}) {
  const listId = useId();
  const [typing, setTyping] = useState<{ from: number; typed: string } | null>(null);
  const [active, setActive] = useState(0);
  const offered = typing === null ? [] : offeredNames(names, typing.typed);
  const open = !locked && offered.length > 0;
  const chosen = Math.min(active, Math.max(0, offered.length - 1));
  const optionId = (index: number) => `${listId}-${String(index)}`;

  const follow = (text: string) => {
    const caret = input.current?.selectionStart ?? text.length;

    setTyping(completionAt(text, caret));
    setActive(0);
  };

  const close = () => {
    setTyping(null);
  };

  const insert = (name: string) => {
    const caret = input.current?.selectionStart ?? value.length;
    const done = complete(value, caret, name);

    setTyping(null);

    if (done === null) {
      return;
    }

    onChange(done.text);
    requestAnimationFrame(() => {
      input.current?.setSelectionRange(done.caret, done.caret);
    });
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (!open) {
      return;
    }

    switch (event.key) {
      case "ArrowDown":
      case "ArrowUp":
        event.preventDefault();
        setActive((chosen + (event.key === "ArrowDown" ? 1 : offered.length - 1)) % offered.length);
        break;
      case "Enter":
      case "Tab": {
        const name = offered[chosen];

        if (name !== undefined) {
          event.preventDefault();
          insert(name);
        }

        break;
      }
      case "Escape":
        // Escape only closes the completion. Stopping it here keeps the inspector from moving the focus out of the
        // field.
        event.stopPropagation();
        setTyping(null);
        break;
    }
  };

  const listProps = open
    ? {
        "aria-controls": listId,
        "aria-activedescendant": optionId(chosen),
      }
    : {};

  return { listId, offered, open, chosen, optionId, follow, close, insert, onKeyDown, listProps };
}
