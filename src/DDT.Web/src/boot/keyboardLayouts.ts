// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n, type MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";

// The keyboard layouts the build offers, each as Windows names it: an input locale and a layout. The agent's sign-in
// in Windows PE is typed with it.
const layouts: { id: string; name: MessageDescriptor }[] = [
  { id: "0405:00000405", name: msg`Czech` },
  { id: "0406:00000406", name: msg`Danish` },
  { id: "0413:00020409", name: msg`Dutch (United States International)` },
  { id: "0813:00000813", name: msg`Dutch (Belgium)` },
  { id: "0809:00000809", name: msg`English (United Kingdom)` },
  { id: "0409:00000409", name: msg`English (United States)` },
  { id: "040b:0000040b", name: msg`Finnish` },
  { id: "040c:0000040c", name: msg`French` },
  { id: "080c:0000080c", name: msg`French (Belgium)` },
  { id: "100c:0000100c", name: msg`French (Switzerland)` },
  { id: "0407:00000407", name: msg`German` },
  { id: "0807:00000807", name: msg`German (Switzerland)` },
  { id: "040e:0000040e", name: msg`Hungarian` },
  { id: "0410:00000410", name: msg`Italian` },
  { id: "0414:00000414", name: msg`Norwegian` },
  { id: "0415:00000415", name: msg`Polish (Programmers)` },
  { id: "0816:00000816", name: msg`Portuguese` },
  { id: "0416:00000416", name: msg`Portuguese (Brazil)` },
  { id: "0c0a:0000040a", name: msg`Spanish` },
  { id: "041d:0000041d", name: msg`Swedish` },
  { id: "041f:0000041f", name: msg`Turkish Q` },
];

// The Select's key for the layout Windows gives the server itself, which a build without one takes.
export const serverLayout = "server";

export interface KeyboardLayoutOption {
  id: string;
  name: string;
}

// The layouts by name, with the one a build recorded added under its identifier when it is none of them.
export function keyboardLayoutOptions(recorded: string | null): KeyboardLayoutOption[] {
  const options = layouts
    .map((layout) => ({ id: layout.id, name: i18n._(layout.name) }))
    .sort((left, right) => left.name.localeCompare(right.name, i18n.locale));

  return recorded === null || known(recorded) !== undefined
    ? options
    : [...options, { id: recorded.toLowerCase(), name: recorded }];
}

// The name of a layout, or its identifier when the list above does not have it.
export function keyboardLayoutName(id: string): string {
  const layout = known(id);

  return layout === undefined ? id : i18n._(layout.name);
}

function known(id: string) {
  return layouts.find((layout) => layout.id === id.toLowerCase());
}
