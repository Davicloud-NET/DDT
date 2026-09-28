// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation } from "@tanstack/react-query";

import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";

import { testOidc } from "../signIn";

import { ProviderResult } from "./ProviderResult";

// Reads the provider's discovery document at the address in the form, before it is saved.
export function ProviderTest({ authority }: { authority: string | null }) {
  const test = useMutation({
    mutationFn: (address: string) => testOidc(address),
  });

  return (
    <div className="flex flex-col gap-3">
      <span>
        <Button
          isDisabled={test.isPending || (authority ?? "").trim() === ""}
          onPress={() => {
            test.mutate((authority ?? "").trim());
          }}
        >
          <Trans>Test the provider</Trans>
        </Button>
      </span>
      {test.isError ? <Notice tone="fail">{test.error.message}</Notice> : null}
      {test.isSuccess ? <ProviderResult result={test.data} /> : null}
    </div>
  );
}
