// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  hashKey,
  useQueryClient,
  type InferDataFromTag,
  type QueryCacheNotifyEvent,
  type QueryClient,
  type QueryKey,
} from "@tanstack/react-query";
import { useCallback, useEffect, useEffectEvent, useState, useSyncExternalStore } from "react";

import { createListeners } from "@/lib/listeners";
import { FLASH_MS } from "@/ui/motion";
import type { StateTone } from "@/ui/StateTag";

// What a change did to one item of a list, for as long as its flash lasts.
export interface LiveMark {
  // The item was not in the list before, so it enters.
  isNew: boolean;
  // The colour it flashes in; null for an item that enters without a flash.
  tone: StateTone | null;
  // Alternates with each mark of the same item, so a second change starts the flash over.
  cycle: 0 | 1;
}

export interface LiveMarkOptions<TKey extends QueryKey, TItem> {
  // The query whose data the list shows.
  queryKey: TKey;
  // The list's items in that data.
  items: (data: InferDataFromTag<unknown, TKey>) => readonly TItem[];
  id: (item: TItem) => string;
  // What the list shows of an item that a change should point out, such as its state. An item whose signature
  // changes flashes; one that changes otherwise, such as a running step's percentage, does not.
  signature: (item: TItem) => string;
  // The colour of the item's state, or null where a change of it needs no flash.
  tone: (item: TItem) => StateTone | null;
}

interface Seen {
  signature: string;
  tone: StateTone | null;
}

// Marks the items a change put on the screen: a changed signature flashes in its tone, and a new item enters. Only
// data from setQueryData counts, as pushes and action answers arrive that way; a read, such as a first load, never
// flashes. The returned function changes with the marks, so it goes into a React Aria collection's dependencies.
export function useLiveMarks<TKey extends QueryKey, TItem>(
  options: LiveMarkOptions<TKey, TItem>,
): (id: string) => string {
  const queryClient = useQueryClient();
  const [store] = useState(createMarkStore);
  const hash = hashKey(options.queryKey);

  const read = useEffectEvent((data: unknown): Map<string, Seen> => {
    const seen = new Map<string, Seen>();

    for (const item of options.items(data as InferDataFromTag<unknown, TKey>)) {
      seen.set(options.id(item), { signature: options.signature(item), tone: options.tone(item) });
    }

    return seen;
  });
  const queryKey = useEffectEvent(() => options.queryKey);

  // A new query key, such as another filter, starts over from what that query holds.
  useEffect(
    () =>
      store.watch(queryClient, queryKey(), (data) => {
        return read(data);
      }),
    [store, queryClient, hash],
  );

  const marks = useSyncExternalStore(store.subscribe, store.marks);

  return useCallback((id: string) => markClass(marks.get(id)), [marks]);
}

// The classes app.css draws a mark with.
function markClass(mark: LiveMark | undefined): string {
  if (mark === undefined) {
    return "";
  }

  const classes = mark.isNew ? ["live-new"] : [];

  if (mark.tone !== null) {
    classes.push("live-flash", `live-tone-${mark.tone}`);
  }

  classes.push(`live-cycle-${String(mark.cycle)}`);

  return classes.join(" ");
}

function createMarkStore() {
  let marks: ReadonlyMap<string, LiveMark> = new Map();
  const listeners = createListeners();

  const publish = (next: ReadonlyMap<string, LiveMark>) => {
    marks = next;
    listeners.notify();
  };

  // Follows one query's data from what it holds now. Returns the unwatch.
  const watch = (
    queryClient: QueryClient,
    queryKey: QueryKey,
    read: (data: unknown) => Map<string, Seen>,
  ): (() => void) => {
    const cache = queryClient.getQueryCache();
    const hash = hashKey(queryKey);
    const cycles = new Map<string, 0 | 1>();
    const timers = new Set<ReturnType<typeof setTimeout>>();
    let data: unknown = cache.get(hash)?.state.data;
    let seen = data === undefined ? null : read(data);

    const expire = (added: ReadonlyMap<string, LiveMark>) => {
      const kept = [...marks].filter(([id, mark]) => added.get(id) !== mark);

      if (kept.length !== marks.size) {
        publish(new Map(kept));
      }
    };

    const changed = (event: QueryCacheNotifyEvent) => {
      if (event.query.queryHash !== hash) {
        return;
      }

      if (event.type === "removed") {
        data = undefined;
        seen = null;
        return;
      }

      if (event.type !== "updated" || event.query.state.data === data) {
        return;
      }

      const before = seen;
      data = event.query.state.data;
      seen = data === undefined ? null : read(data);

      if (
        before === null ||
        seen === null ||
        event.action.type !== "success" ||
        event.action.manual !== true
      ) {
        return;
      }

      const added = new Map<string, LiveMark>();

      for (const [id, now] of seen) {
        const was = before.get(id);

        if (was !== undefined && (was.signature === now.signature || now.tone === null)) {
          continue;
        }

        const cycle = cycles.get(id) === 1 ? 0 : 1;
        cycles.set(id, cycle);
        added.set(id, { isNew: was === undefined, tone: now.tone, cycle });
      }

      if (added.size === 0) {
        return;
      }

      publish(new Map([...marks, ...added]));

      const timer = setTimeout(() => {
        timers.delete(timer);
        expire(added);
      }, FLASH_MS);
      timers.add(timer);
    };

    const unsubscribe = cache.subscribe(changed);

    return () => {
      unsubscribe();

      for (const timer of timers) {
        clearTimeout(timer);
      }

      if (marks.size > 0) {
        publish(new Map());
      }
    };
  };

  return {
    subscribe: listeners.subscribe,
    marks: () => marks,
    watch,
  };
}
