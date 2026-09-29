// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { Notice } from "@/ui/Notice";
import { Skeleton } from "@/ui/Skeleton";
import { showToast } from "@/ui/toasts";

import { DesignSection } from "./DesignSection";

interface FeedbackSectionProps {
  onConfirm: (kind: "plain" | "erase") => void;
  onDrawer: () => void;
}

export function FeedbackSection({ onConfirm, onDrawer }: FeedbackSectionProps) {
  return (
    <DesignSection title="Feedback">
      <Notice tone="fail" title="BUILD-VM-02 failed at step 2">
        The image is not signed under a CA this machine trusts.
      </Notice>
      <Notice tone="attention">The live connection to the server is lost.</Notice>
      <Notice title="Nothing needs you">Every machine is deploying or done.</Notice>
      <div className="flex flex-wrap gap-2">
        <Button
          onPress={() => {
            showToast({
              title: "ACC-PC-004 is done",
              description: "Windows 11 24H2 with Office took 36 minutes.",
              tone: "ok",
            });
          }}
        >
          Toast
        </Button>
        <Button
          onPress={() => {
            showToast({
              title: "BUILD-VM-02 failed",
              description: "Step 2: not signed for this firmware.",
              tone: "fail",
            });
          }}
        >
          Failure toast
        </Button>
        <Button
          onPress={() => {
            onConfirm("plain");
          }}
        >
          Confirm
        </Button>
        <Button
          variant="danger"
          onPress={() => {
            onConfirm("erase");
          }}
        >
          Erase
        </Button>
        <Button onPress={onDrawer}>Drawer</Button>
      </div>
      <EmptyState title="No machines yet" action={<Button>How netboot works</Button>}>
        Netboot a PC on a network DDT answers. It shows up here within a few seconds.
      </EmptyState>
      <Skeleton className="h-4 w-2/3" />
      <Skeleton className="h-4 w-1/2" />
    </DesignSection>
  );
}
