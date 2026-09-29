// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { canonicalArchitecture, type PxeForm } from "../networkBoot";

// The problems of a boot target's field, which the server names like bootTargets[X64Uefi].bootFile. For a key typed in
// another case, some checks use the stored key and others the member name, so both are looked up.
export function targetErrors(form: PxeForm, key: string, field: string): string[] {
  const canonical = canonicalArchitecture(key);
  const suffix = field === "" ? "" : `.${field}`;
  const names = [`bootTargets[${key}]${suffix}`];

  if (canonical !== null && canonical !== key) {
    names.push(`bootTargets[${canonical}]${suffix}`);
  }

  return names.flatMap((name) => form.fieldErrors(name));
}

// The form as a boot target's shared fields see it. bootTargets.X64Uefi.serverAddress reads and changes the value. Its
// problems are looked up under the server's name for it. The collection's lock shows only once, above the targets.
export function targetForm(form: PxeForm): PxeForm {
  return {
    ...form,
    fieldErrors: (field: string) => {
      const [, key = "", ...rest] = field.split(".");

      return field.startsWith("bootTargets.")
        ? targetErrors(form, key, rest.join("."))
        : form.fieldErrors(field);
    },
    lockOf: () => null,
  };
}
