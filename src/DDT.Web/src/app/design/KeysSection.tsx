// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Button } from "@/ui/Button";
import { Tooltip } from "@/ui/Tooltip";

import { DesignSection } from "./DesignSection";

export function KeysSection() {
  return (
    <DesignSection title="Keys">
      <div className="flex flex-wrap gap-2">
        <Button variant="primary">Assign sequence</Button>
        <Button>Approve</Button>
        <Button variant="quiet">Cancel</Button>
        <Button variant="danger">Stop run</Button>
        <Button isDisabled>Reject</Button>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button variant="primary" size="sm">
          Approve
        </Button>
        <Button size="sm">Assign</Button>
        <Tooltip content="Explains the key it sits on.">
          <Button size="sm" variant="quiet">
            With a tooltip
          </Button>
        </Tooltip>
      </div>
    </DesignSection>
  );
}
