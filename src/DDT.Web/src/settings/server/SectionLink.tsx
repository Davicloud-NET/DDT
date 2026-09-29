// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

const LINK_CLASS = "text-ink underline underline-offset-3 hover:text-run-text";

// The page that edits a section. The certificate section holds the server names.
export function SectionLink({ section }: { section: string }) {
  switch (section) {
    case "deployment":
      return (
        <Link to="/deployment/defaults" className={LINK_CLASS}>
          <Trans>Deployment defaults</Trans>
        </Link>
      );
    case "machines":
      return (
        <Link to="/machines/approval" className={LINK_CLASS}>
          <Trans>Approval and zero touch</Trans>
        </Link>
      );
    case "pxe":
      return (
        <Link to="/boot/network" className={LINK_CLASS}>
          <Trans>Network boot</Trans>
        </Link>
      );
    case "ldap":
      return (
        <Link to="/admin/sign-in" className={LINK_CLASS}>
          <Trans>Directory sign-in (LDAP)</Trans>
        </Link>
      );
    case "oidc":
      return (
        <Link to="/admin/sign-in" className={LINK_CLASS}>
          <Trans>Single sign-on (OpenID Connect)</Trans>
        </Link>
      );
    case "certificate":
      return (
        <Link to="/admin/server" search={{ tab: "certificate" }} className={LINK_CLASS}>
          <Trans>Server names</Trans>
        </Link>
      );
    case "proxies":
      return (
        <Link to="/admin/server" search={{ tab: "proxies" }} className={LINK_CLASS}>
          <Trans>Proxies</Trans>
        </Link>
      );
    case "logging":
      return (
        <Link to="/admin/server" search={{ tab: "logging" }} className={LINK_CLASS}>
          <Trans>Logging</Trans>
        </Link>
      );
    default:
      return <span className="type-data text-ink">{section}</span>;
  }
}
