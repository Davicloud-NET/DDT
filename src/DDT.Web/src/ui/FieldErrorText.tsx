// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The server's errors for a field that isn't a React Aria field, since those show their own. Renders nothing when there
// are none.
export function FieldErrorText({ errors }: { errors: readonly string[] }) {
  return errors.length > 0 ? (
    <span className="type-small text-fail-text">{errors.join(" ")}</span>
  ) : null;
}
