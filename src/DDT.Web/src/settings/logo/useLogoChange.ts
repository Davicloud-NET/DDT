// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";

import {
  consoleLogoQuery,
  removeConsoleLogo,
  uploadConsoleLogo,
  type ConsoleLogoView,
} from "../consoleLogo";
import { useGuardedAction } from "../useGuardedAction";

import { logoProblem } from "./logoProblem";

// Uploads a chosen logo or removes the current one, and puts the server's answer into the cache.
export function useLogoChange() {
  const queryClient = useQueryClient();
  // The file being sent, which the upload reads when it starts; its name stays for the answer.
  const chosen = useRef<File | null>(null);
  const [name, setName] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [done, setDone] = useState<"uploaded" | "removed" | null>(null);
  const [confirmingRemoval, setConfirmingRemoval] = useState(false);

  const settle = (answer: ConsoleLogoView) => {
    queryClient.setQueryData(consoleLogoQuery.queryKey, answer);
  };

  const upload = useGuardedAction({
    send: () => {
      if (chosen.current === null) {
        throw new Error(t`Choose the logo first.`);
      }

      return uploadConsoleLogo(chosen.current);
    },
    onDone: (answer) => {
      settle(answer);
      setDone("uploaded");
      chosen.current = null;
    },
  });

  const removal = useGuardedAction({
    send: () => removeConsoleLogo(),
    onDone: (answer) => {
      settle(answer);
      setDone("removed");
    },
  });

  const busy = upload.busy || removal.busy;

  const clear = () => {
    upload.reset();
    removal.reset();
    setDone(null);
    setProblem(null);
  };

  // A chosen file is sent at once; a wrong one is replaced or removed just as quickly.
  const pick = (file: File | undefined) => {
    if (file === undefined || busy) {
      return;
    }

    const refused = logoProblem(file);

    clear();

    if (refused !== null) {
      setProblem(refused);
    } else {
      chosen.current = file;
      setName(file.name);
      upload.start();
    }
  };

  return {
    upload,
    removal,
    busy,
    name,
    problem,
    done,
    confirmingRemoval,
    setConfirmingRemoval,
    pick,
    askRemoval: () => {
      clear();
      setConfirmingRemoval(true);
    },
    remove: () => {
      setConfirmingRemoval(false);
      removal.start();
    },
  };
}
