// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { DesignSection } from "./DesignSection";

export function TypeSection() {
  return (
    <DesignSection title="Type">
      <span className="type-display text-run-text">62%</span>
      <span className="type-title">All machines</span>
      <span className="type-heading">Windows 11 24H2 with Office</span>
      <span className="type-label">Assign sequence</span>
      <span className="type-body">
        Leave this machine on. It restarts by itself and finishes in Windows.
      </span>
      <span className="type-small text-muted">Registered at 09:51 from 10.20.4.130</span>
      <span className="type-data">3C:52:82:6A:1F:0B</span>
    </DesignSection>
  );
}
