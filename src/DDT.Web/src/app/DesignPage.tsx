// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { Button } from "@/ui/Button";
import { ConfirmDialog } from "@/ui/ConfirmDialog";
import { Drawer } from "@/ui/Drawer";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";

import { ColourSection } from "./design/ColourSection";
import { FactsSection } from "./design/FactsSection";
import { FeedbackSection } from "./design/FeedbackSection";
import { FieldsSection } from "./design/FieldsSection";
import { FlowSection } from "./design/FlowSection";
import { KeysSection } from "./design/KeysSection";
import { LiveSection } from "./design/LiveSection";
import { RailSection } from "./design/RailSection";
import { StateSection } from "./design/StateSection";
import { TableSection } from "./design/TableSection";
import { TabsSection } from "./design/TabsSection";
import { TypeSection } from "./design/TypeSection";

// Every token and component in one place, in the theme and language chosen in the user menu. It exists in
// development builds only and is not translated.
export function DesignPage() {
  const [confirm, setConfirm] = useState<"plain" | "erase" | null>(null);
  const [drawer, setDrawer] = useState(false);

  return (
    <Page>
      <PageHeader title="Design system">
        <span className="text-muted">Switchgear, from src/DDT.Design/tokens.json</span>
      </PageHeader>

      <ColourSection />
      <TypeSection />

      <div className="grid gap-4 lg:grid-cols-2">
        <KeysSection />
        <StateSection />
      </div>

      <FlowSection />
      <RailSection />

      <div className="grid gap-4 lg:grid-cols-2">
        <FieldsSection />
        <FeedbackSection
          onConfirm={setConfirm}
          onDrawer={() => {
            setDrawer(true);
          }}
        />
      </div>

      <FactsSection />
      <TableSection />
      <LiveSection />
      <TabsSection />

      <ConfirmDialog
        isOpen={confirm !== null}
        onOpenChange={(open) => {
          if (!open) setConfirm(null);
        }}
        title={confirm === "erase" ? "Erase disk 0 on LAB-PC-016?" : "Stop the run on LAB-PC-014?"}
        confirmLabel={confirm === "erase" ? "Erase and run" : "Stop the run"}
        danger
        {...(confirm === "erase" ? { typedWord: "ERASE" } : {})}
        onConfirm={() => {
          setConfirm(null);
        }}
      >
        {confirm === "erase" ? (
          <p>
            Windows 11 24H2 with Office removes every partition on disk 0, a SK hynix PC801 with 512
            GB.
          </p>
        ) : (
          <p>The machine stops after the step it is on. Nothing already written is undone.</p>
        )}
      </ConfirmDialog>

      <Drawer
        isOpen={drawer}
        onOpenChange={setDrawer}
        title="LAB-PC-014"
        footer={<Button variant="primary">Open machine</Button>}
      >
        <p className="text-ink-2">Details of the machine picked in the list.</p>
      </Drawer>
    </Page>
  );
}
