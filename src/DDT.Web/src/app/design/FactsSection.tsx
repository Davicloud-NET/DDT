// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Facts } from "@/ui/Facts";

import { DesignSection } from "./DesignSection";

export function FactsSection() {
  return (
    <DesignSection title="Facts">
      <Facts
        layout="plate"
        items={[
          { label: "Serial", value: "7XK2QH3", mono: true },
          { label: "MAC address", value: "3C:52:82:6A:1F:0B", mono: true },
          { label: "Address", value: "10.20.4.114", mono: true },
          { label: "Secure Boot", value: "On, trusts UEFI CA 2023" },
        ]}
      />
      <Facts
        items={[
          { label: "Erases", value: "Disk 0, SK hynix PC801, 512 GB" },
          { label: "Chosen", value: "At the machine by m.huber" },
        ]}
      />
    </DesignSection>
  );
}
