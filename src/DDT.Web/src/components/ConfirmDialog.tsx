import { Dialog } from "./Dialog";

import styles from "./ConfirmDialog.module.scss";

export interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  // What happens when the action is confirmed, in plain sentences.
  consequence: string;
  confirmLabel: string;
  busy: boolean;
  error: string | null;
  onConfirm: () => void;
}

// Asks before an action that cannot be undone. Focus starts on Close, so Enter alone never confirms.
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  consequence,
  confirmLabel,
  busy,
  error,
  onConfirm,
}: ConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!busy) {
          onOpenChange(next);
        }
      }}
      title={title}
      description={consequence}
    >
      {error !== null && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}
      <div className={styles.actions}>
        <button
          type="button"
          className={styles.secondary}
          disabled={busy}
          onClick={() => {
            onOpenChange(false);
          }}
        >
          Close
        </button>
        <button type="button" className={styles.confirm} disabled={busy} onClick={onConfirm}>
          {confirmLabel}
        </button>
      </div>
    </Dialog>
  );
}
