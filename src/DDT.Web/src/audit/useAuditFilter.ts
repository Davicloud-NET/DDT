// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useState } from "react";

import type { AuditFilter } from "./audit";

// The audit log's filter fields, and the filter the server reads with.
export function useAuditFilter() {
  const [action, setAction] = useState("");
  const [typedActor, setTypedActor] = useState("");
  const [actor, setActor] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  // The server searches the actor, so the search waits until typing pauses. Not useDebouncedValue, since clearing
  // the filter clears the actor at once.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setActor(typedActor);
    }, 300);

    return () => {
      window.clearTimeout(timer);
    };
  }, [typedActor]);

  const filter: AuditFilter = { action, actor, from, to };

  return {
    filter,
    filtered: action !== "" || actor !== "" || from !== "" || to !== "",
    action,
    setAction,
    typedActor,
    setTypedActor,
    from,
    setFrom,
    to,
    setTo,
    clear: () => {
      setAction("");
      setTypedActor("");
      setActor("");
      setFrom("");
      setTo("");
    },
  };
}

export type AuditFilterFields = ReturnType<typeof useAuditFilter>;
