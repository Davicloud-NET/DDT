// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { acknowledgeReplacedAnchor, serverCertificateQuery } from "@/server/serverCertificate";
import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";

// Shown after DDT replaced the self-signed certificate that older boot images pin. It stays until an administrator
// confirms that every boot image was built again with DDT's root.
export function ReplacedAnchor() {
  const queryClient = useQueryClient();
  const certificate = useQuery(serverCertificateQuery).data ?? null;
  const acknowledge = useMutation({
    mutationFn: acknowledgeReplacedAnchor,
    onSuccess: () => {
      queryClient.setQueryData(serverCertificateQuery.queryKey, (current) =>
        current === undefined || current === null
          ? current
          : { ...current, anchorReplacedUtc: null },
      );
    },
  });

  if (certificate?.anchorReplacedUtc == null) {
    return null;
  }

  return (
    <Notice tone="attention">
      <span className="flex flex-col gap-2">
        <Trans>
          DDT replaced the self-signed server certificate that boot images built before it pin.
          Machines netbooting such an image cannot reach the server until it is built again with
          DDT's root certificate.
        </Trans>
        <span>
          <Button
            size="sm"
            isDisabled={acknowledge.isPending}
            onPress={() => {
              acknowledge.mutate();
            }}
          >
            <Trans>Every boot image is built again</Trans>
          </Button>
        </span>
      </span>
    </Notice>
  );
}
