// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconChevronRight } from "@tabler/icons-react";
import { Fragment } from "react";

import type { PathNode } from "../runPath";
import { crumbText } from "../runView";

// Where a node sits in the tree: the containers around it, outermost first, and the branch of an IF.
export function Crumbs({ node }: { node: PathNode }) {
  return (
    <span className="flex flex-wrap items-center gap-x-1 type-small text-muted">
      <span className="sr-only">{crumbText(node.ancestors)}</span>
      {node.ancestors.map((ancestor, index) => (
        <Fragment key={ancestor.node.id}>
          {index > 0 ? <IconChevronRight aria-hidden="true" size={12} stroke={2} /> : null}
          <span aria-hidden="true">{crumbText([ancestor])}</span>
        </Fragment>
      ))}
    </span>
  );
}
