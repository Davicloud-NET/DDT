import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";

import { machinesQuery, upsertMachine, type MachineSummary } from "@/machines/machines";

function backoff(attempt: number): number {
  return Math.min(30_000, 1_000 * 2 ** attempt);
}

// One connection for the signed in application. Events patch the query cache directly. Anything sent
// while disconnected is lost, so every successful connect refetches what the events would have patched.
// Automatic reconnect covers a dropped connection but not a failed first start, so that is retried here.
export function useLiveUpdates(): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/live")
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retry) => backoff(retry.previousRetryCount),
      })
      .configureLogging(LogLevel.Warning)
      .build();

    let disposed = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;

    const resync = () => {
      void queryClient.invalidateQueries({ queryKey: machinesQuery.queryKey });
    };

    const start = async (attempt: number): Promise<void> => {
      try {
        await connection.start();
        resync();
      } catch {
        if (!disposed) {
          retryTimer = setTimeout(() => void start(attempt + 1), backoff(attempt));
        }
      }
    };

    connection.on("machineChanged", (machine: MachineSummary) => {
      upsertMachine(queryClient, machine);
    });

    connection.on("machinesRemoved", resync);

    connection.onreconnected(resync);

    connection.onclose(() => {
      if (!disposed) {
        void start(0);
      }
    });

    void start(0);

    return () => {
      disposed = true;
      clearTimeout(retryTimer);
      void connection.stop();
    };
  }, [queryClient]);
}
