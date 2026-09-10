import styles from "./MachinesPage.module.scss";

export function MachinesPage() {
  return (
    <div className={styles.page}>
      <h1>Machines</h1>
      <section className={styles.empty}>
        <h2 className={styles.emptyTitle}>No machines yet</h2>
        <p>
          Machines appear here once the DDT agent checks in. Nothing has registered with this
          server.
        </p>
      </section>
    </div>
  );
}
