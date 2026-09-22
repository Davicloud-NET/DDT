// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useState, useSyncExternalStore } from "react";

import { createAutosaver, type AutosaveOptions } from "./autosave";

// Saves a document in place while it is edited. The options are read once, so the component that uses this is
// keyed by the document it edits. Leaving the page sends what is not saved yet; closing or reloading it asks
// first while something is unsaved, and sends it as the page goes.
export function useAutosave<T, R>(options: AutosaveOptions<T, R>) {
  const [saver] = useState(() => createAutosaver(options));
  const snapshot = useSyncExternalStore(saver.subscribe, saver.snapshot);

  useEffect(() => {
    saver.start();

    const onPageHide = () => {
      void saver.flush(true);
    };

    window.addEventListener("pagehide", onPageHide);

    return () => {
      window.removeEventListener("pagehide", onPageHide);
      saver.close();
    };
  }, [saver]);

  useEffect(() => {
    if (!snapshot.dirty) {
      return;
    }

    const warn = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };

    window.addEventListener("beforeunload", warn);

    return () => {
      window.removeEventListener("beforeunload", warn);
    };
  }, [snapshot.dirty]);

  return {
    ...snapshot,
    update: saver.update,
    receive: saver.receive,
    flush: saver.flush,
    takeTheirs: saver.takeTheirs,
    keepMine: saver.keepMine,
    stop: saver.stop,
  };
}
