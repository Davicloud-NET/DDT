import * as RadixDialog from "@radix-ui/react-dialog";
import type { ReactNode } from "react";

import styles from "./Dialog.module.scss";

export interface DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  // Read out when the dialog opens, so it states what the dialog is about.
  description: string;
  children: ReactNode;
}

// A modal dialog: focus stays inside while it is open, Escape closes it, and focus returns afterwards.
export function Dialog({ open, onOpenChange, title, description, children }: DialogProps) {
  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className={styles.overlay} />
        <RadixDialog.Content className={styles.content}>
          <RadixDialog.Title className={styles.title}>{title}</RadixDialog.Title>
          <RadixDialog.Description className={styles.description}>
            {description}
          </RadixDialog.Description>
          {children}
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  );
}
