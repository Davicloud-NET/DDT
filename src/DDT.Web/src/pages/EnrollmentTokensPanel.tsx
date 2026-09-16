import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { useNow } from "@/lib/useNow";

import {
  createEnrollmentToken,
  enrollmentTokensQuery,
  revokeEnrollmentToken,
  type CreatedEnrollmentToken,
} from "@/machines/enrollmentTokens";

import styles from "./EnrollmentTokensPanel.module.scss";

export function EnrollmentTokensPanel() {
  const queryClient = useQueryClient();
  const tokens = useQuery(enrollmentTokensQuery);
  const now = useNow(60_000);
  const [name, setName] = useState("");
  const [validForDays, setValidForDays] = useState(30);
  const [created, setCreated] = useState<CreatedEnrollmentToken | null>(null);

  const create = useMutation({
    mutationFn: () => createEnrollmentToken(name, validForDays),
    onSuccess: async (result) => {
      setCreated(result);
      setName("");
      await queryClient.invalidateQueries({ queryKey: enrollmentTokensQuery.queryKey });
    },
  });

  const revoke = useMutation({
    mutationFn: revokeEnrollmentToken,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: enrollmentTokensQuery.queryKey });
    },
  });

  return (
    <section className={styles.panel} aria-labelledby="enrollment-tokens">
      <h2 id="enrollment-tokens">Enrollment tokens</h2>
      <p className={styles.hint}>
        The boot image carries one of these so a machine can register. Anyone who downloads the boot
        image can read it, so it only lets a machine register and someone at it try to sign in.
      </p>

      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault();
          create.mutate();
        }}
      >
        <label className={styles.field}>
          Name
          <input
            value={name}
            maxLength={64}
            required
            onChange={(event) => {
              setName(event.target.value);
            }}
          />
        </label>
        <label className={styles.field}>
          Valid for days
          <input
            type="number"
            min={1}
            max={90}
            value={validForDays}
            onChange={(event) => {
              setValidForDays(Number(event.target.value));
            }}
          />
        </label>
        <button type="submit" disabled={create.isPending}>
          Create token
        </button>
      </form>

      {create.isError && <p className={styles.error}>{create.error.message}</p>}

      {created && (
        <div className={styles.created} role="status">
          <p>
            Token for {created.summary.name}. It is shown only now: pass it to Build-BootImage.ps1
            with -EnrollmentToken.
          </p>
          <code className={styles.token}>{created.token}</code>
        </div>
      )}

      {tokens.data && tokens.data.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Expires</th>
              <th scope="col">Status</th>
              <th scope="col" />
            </tr>
          </thead>
          <tbody>
            {tokens.data.map((token) => {
              const expired = new Date(token.expiresUtc).getTime() <= now;

              return (
                <tr key={token.id}>
                  <td>{token.name}</td>
                  <td>{new Date(token.expiresUtc).toLocaleDateString()}</td>
                  <td>{token.revokedUtc ? "Revoked" : expired ? "Expired" : "Active"}</td>
                  <td>
                    {!token.revokedUtc && (
                      <button
                        type="button"
                        disabled={revoke.isPending}
                        onClick={() => {
                          revoke.mutate(token.id);
                        }}
                      >
                        Revoke
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}
