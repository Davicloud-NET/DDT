// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";

import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";

import { currentUserQuery, type CurrentUser } from "./auth";
import { useIsAdministrator } from "./useIsAdministrator";

// A page only administrators can use. Everyone else sees its title and why, since the server refuses them anyway.
export function AdministratorsOnly({
  title,
  refusal,
  className,
  children,
}: {
  title: ReactNode;
  // Why the page shows nothing, and where to look instead.
  refusal: ReactNode;
  // The Page class for the refusal, so it can match the real page's class.
  className?: string;
  children: (me: CurrentUser) => ReactNode;
}) {
  const me = useQuery(currentUserQuery).data ?? null;
  const administrator = useIsAdministrator();

  if (me === null) {
    return null;
  }

  if (!administrator) {
    return (
      <Page {...(className === undefined ? {} : { className })}>
        <PageHeader title={title} />
        <Notice>{refusal}</Notice>
      </Page>
    );
  }

  return children(me);
}
