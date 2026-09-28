// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { formattingLocale } from "@/i18n/i18n";
import { ApiError } from "@/lib/api";
import { relativeTimeAhead } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";

import { confirmCertificate, putCertificate } from "../certificate";

// A new pair on trial, which only a connection that was served it can keep.
export function ProvisionalNotice({ until }: { until: string }) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const now = useNow(5_000);
  const deadline = new Date(until).toLocaleTimeString(formattingLocale());
  const left = relativeTimeAhead(until, now);
  const confirm = useMutation({
    mutationFn: confirmCertificate,
    onSuccess: (answer) => {
      putCertificate(queryClient, answer);
    },
  });

  return (
    <Notice
      tone="attention"
      title={<Trans>The new certificate is on trial until {deadline}</Trans>}
      actions={
        <Button
          size="sm"
          variant="primary"
          isDisabled={confirm.isPending}
          onPress={() => {
            confirm.mutate();
          }}
        >
          <Trans>Keep the new certificate</Trans>
        </Button>
      }
    >
      <Trans>
        This page's next request gets the new certificate. Once the page works with it, keep it
        here. If nobody keeps it by {deadline}, {left}, DDT goes back to the certificate before, so
        a certificate your browser refuses cannot lock you out.
      </Trans>
      {confirm.isError ? (
        <span className="mt-1.5 block font-semibold">
          {confirm.error instanceof ApiError
            ? confirm.error.message
            : t`The server could not be reached with the new certificate, so this browser probably does not trust it. At ${deadline} DDT goes back to the certificate before.`}
        </span>
      ) : null}
    </Notice>
  );
}
