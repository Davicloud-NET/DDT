import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";

import { Dialog } from "@/components/Dialog";
import {
  assignImage,
  deploymentOptionsQuery,
  type AssignImageRequest,
  type DeploymentOptionsView,
} from "@/deployments/deployments";
import { imagesQuery, isDeployable } from "@/images/images";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import {
  machineLabel,
  machinesQuery,
  upsertMachine,
  WAITING_WINDOW_MS,
  type MachineSummary,
} from "@/machines/machines";

import styles from "./AssignDialog.module.scss";

export interface AssignDialogProps {
  machine: MachineSummary;
  onClose: () => void;
}

// Assigns an image and says what that does to this machine before anything is sent: which disk is erased,
// and for a waiting machine whether the assignment also authorizes it.
export function AssignDialog({ machine, onClose }: AssignDialogProps) {
  const queryClient = useQueryClient();
  const images = useQuery(imagesQuery);
  const options = useQuery(deploymentOptionsQuery);
  const now = useNow(5_000);
  const imageFieldId = useId();
  const nameFieldId = useId();
  const nameHintId = useId();

  const [imageId, setImageId] = useState("");
  const [computerName, setComputerName] = useState(machine.assignedName ?? "");
  const [problem, setProblem] = useState<string | null>(null);

  const assign = useMutation({
    mutationFn: (request: AssignImageRequest) => assignImage(machine.id, request),
    onSuccess: (updated) => {
      upsertMachine(queryClient, updated);
      onClose();
    },
    // The refusal usually means the machine changed meanwhile, so show what is stored now.
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: machinesQuery.queryKey });
    },
  });

  const label = machineLabel(machine);
  const deployable = (images.data ?? []).filter(isDeployable);
  const image = deployable.find((candidate) => candidate.id === imageId) ?? deployable[0] ?? null;
  const domainConfigured = options.data?.domainConfigured === true;
  const severalDisks = machine.eligibleDiskCount !== null && machine.eligibleDiskCount > 1;
  const error = problem ?? (assign.isError ? assign.error.message : null);
  // Without the settings the dialog cannot say what the assignment does, so it does not offer it.
  const canSubmit =
    image !== null &&
    !severalDisks &&
    !assign.isPending &&
    options.data !== undefined &&
    !images.isPending;

  function submit() {
    if (image === null) {
      return;
    }

    const name = computerName.trim();

    if (domainConfigured && name === "") {
      setProblem("Enter a computer name. Machines join the domain under this name.");
      return;
    }

    setProblem(null);
    assign.mutate({ imageId: image.id, computerName: name === "" ? null : name });
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !assign.isPending) {
          onClose();
        }
      }}
      title={`Assign an image to ${label}`}
      description="Choose the image to install and the name the computer gets."
    >
      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <div className={styles.field}>
          <label htmlFor={imageFieldId}>Image</label>
          <select
            id={imageFieldId}
            value={image?.id ?? ""}
            disabled={deployable.length === 0}
            onChange={(event) => {
              setImageId(event.target.value);
            }}
          >
            {deployable.map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {[candidate.name, candidate.language, candidate.version]
                  .filter((part) => part !== null)
                  .join(", ")}
              </option>
            ))}
          </select>
        </div>

        {images.isError && <p className={styles.error}>The image list could not be loaded.</p>}
        {options.isError && (
          <p className={styles.error}>
            The deployment settings could not be loaded, so the dialog cannot say what the
            assignment does. Close it and try again.
          </p>
        )}
        {images.isSuccess && deployable.length === 0 && (
          <p className={styles.hint}>
            The library has no x64 image to deploy. An administrator uploads one on the Images page.
          </p>
        )}

        <div className={styles.field}>
          <label htmlFor={nameFieldId}>Computer name</label>
          <input
            id={nameFieldId}
            value={computerName}
            maxLength={15}
            autoComplete="off"
            spellCheck={false}
            required={domainConfigured}
            aria-describedby={nameHintId}
            onChange={(event) => {
              setComputerName(event.target.value);
            }}
          />
          <span id={nameHintId} className={styles.hint}>
            {domainConfigured
              ? "Required, because machines join the domain under this name."
              : machine.assignedName === null
                ? "Optional. Without a name, Windows picks one."
                : `Optional. Left empty, the machine keeps the name ${machine.assignedName}.`}{" "}
            Up to 15 letters A to Z, digits and hyphens.
          </span>
        </div>

        <div className={styles.consequences}>
          {image !== null && (
            <p className={styles.warning}>
              All data on the disk of {label} will be erased and {image.name} installed.
            </p>
          )}
          <p>
            Model: {machine.model ?? "not reported"}.{" "}
            {machine.disks === null
              ? "The machine has not reported its disks."
              : `Reported disks: ${machine.disks}.`}
          </p>
          {severalDisks && (
            <p className={styles.error}>
              This machine has more than one disk. Sign in at it and choose the disk there.
            </p>
          )}
          {machine.eligibleDiskCount === 0 && (
            <p className={styles.error}>
              The machine reported no disk DDT can install on, so the deployment will fail.
            </p>
          )}
          {machine.state === "Pending" && (
            <>
              <p>
                Last seen
                {machine.lastSeenAddress === null ? "" : ` from ${machine.lastSeenAddress}`}{" "}
                {relativeTime(machine.lastSeenUtc, now)};{" "}
                {machine.signedInBy === null
                  ? "nobody has signed in at it."
                  : `signed in by ${machine.signedInBy}.`}
              </p>
              {options.data !== undefined && (
                <p>{pendingConsequence(machine, options.data, now)}</p>
              )}
            </>
          )}
        </div>

        {error !== null && (
          <p className={styles.error} role="alert">
            {error}
          </p>
        )}

        <div className={styles.actions}>
          <button
            type="button"
            className={styles.secondary}
            disabled={assign.isPending}
            onClick={onClose}
          >
            Close
          </button>
          <button type="submit" className={styles.submit} disabled={!canSubmit}>
            Assign image
          </button>
        </div>
      </form>
    </Dialog>
  );
}

// What the assignment does to a machine that is not authorized yet (design 1.2 and 1.3). Without web
// approval, the assignment authorizes a machine waiting at the prompt; one not seen for a while is
// authorized by the next sign-in at it, or by its next netboot from a zero touch network. With web
// approval, zero touch is off and a sign-in authorizes the machine once it has an assigned image. The agent
// offers the sign-in only until somebody signed in, and approving on this page needs that sign-in.
function pendingConsequence(
  machine: MachineSummary,
  options: DeploymentOptionsView,
  now: number,
): string {
  if (options.requireWebApproval) {
    return machine.signedInBy === null
      ? "It stays waiting until someone signs in at it."
      : "It stays waiting until someone approves it on this page.";
  }

  if (now - Date.parse(machine.lastSeenUtc) <= WAITING_WINDOW_MS) {
    return "This also authorizes the machine, which then receives the image and the deployment passwords.";
  }

  return options.zeroTouchEnabled
    ? "It stays waiting until someone signs in at it or it netboots from a zero touch network."
    : "It stays waiting until someone signs in at it.";
}
