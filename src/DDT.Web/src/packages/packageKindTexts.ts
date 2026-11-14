// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { PackageKind } from "./packages";

export interface PackageKindTexts {
  title: string;
  table: string;
  find: string;
  placeholder: string;
  empty: string;
}

// What a page of one kind of package calls things.
export function packageKindTexts(kind: PackageKind): PackageKindTexts {
  switch (kind) {
    case "Drivers":
      return {
        title: t`Drivers`,
        table: t`Driver packages`,
        find: t`Find a driver package`,
        placeholder: t`Name, model or file`,
        empty: t`No driver packages yet`,
      };
    case "WindowsPackage":
      return {
        title: t`Windows packages`,
        table: t`Windows packages`,
        find: t`Find a Windows package`,
        placeholder: t`Name, language or KB`,
        empty: t`No Windows packages yet`,
      };
    default:
      return {
        title: t`Files`,
        table: t`File packages`,
        find: t`Find a file package`,
        placeholder: t`Name or file`,
        empty: t`No file packages yet`,
      };
  }
}
