// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useLayoutEffect, useRef, useState } from "react";

import { followingAfterScroll } from "@/log/logRows";

// What a job wrote, on the console well. It stays at the newest line until someone scrolls up, and again once they
// are back at the end.
export function JobOutput({
  lines,
  missing,
  isRunning,
}: {
  lines: readonly string[];
  missing: number;
  isRunning: boolean;
}) {
  const { t: translate } = useLingui();
  const output = useRef<HTMLPreElement>(null);
  const [following, setFollowing] = useState(true);

  useLayoutEffect(() => {
    if (following && output.current !== null) {
      output.current.scrollTop = output.current.scrollHeight;
    }
  }, [lines, following]);

  if (lines.length === 0) {
    return null;
  }

  return (
    <>
      <pre
        ref={output}
        role="log"
        aria-live="off"
        aria-label={translate`Output`}
        tabIndex={0}
        className="max-h-[22rem] overflow-auto rounded-key bg-console px-3.5 py-3 font-mono text-[12.5px] leading-5 text-console-text outline-none focus-visible:outline-2 focus-visible:outline-focus"
        onScroll={(event) => {
          const element = event.currentTarget;

          setFollowing(
            followingAfterScroll(
              following,
              element.scrollHeight - element.scrollTop - element.clientHeight,
            ),
          );
        }}
      >
        {lines.join("\n")}
      </pre>
      {!isRunning && missing > 0 ? (
        <p className="type-small text-muted">
          <Trans>The output went on for {missing} more lines, which the server did not keep.</Trans>
        </p>
      ) : null}
    </>
  );
}
