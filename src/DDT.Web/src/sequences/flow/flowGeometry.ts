// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { BodyName, Slot } from "./flowTree";

export const NODE_WIDTH = 236;
export const LEAF_HEIGHT = 64;
export const IF_HEIGHT = 100;
// A group's or a repeat's header card, and a container shown collapsed.
export const HEADER_HEIGHT = 84;
// The Then and Else ports sit this far in from the sides of an IF's card.
export const PORT_INSET = 25;
// An arrowhead ends 1 px before the node it points at; its wire ends at its base.
export const ARROW_LENGTH = 8;
export const ARROW_HALF_WIDTH = 5;
export const LOOP_RADIUS = 12;

export interface Point {
  x: number;
  y: number;
}

export interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export type FlowBoxKind = "leaf" | "if" | "group" | "repeat" | "collapsed";

// A node's card. extent is all the node takes: the card for a leaf, the frame for a group or a repeat, the card,
// the branches and the join for an IF.
export interface FlowBox extends Rect {
  id: string;
  kind: FlowBoxKind;
  parent: string | null;
  body: BodyName;
  depth: number;
  extent: Rect;
}

export interface FlowFrame extends Rect {
  id: string;
  kind: "group" | "repeat";
  depth: number;
}

export interface FlowPort extends Point {
  id: string;
  branch: "then" | "else";
}

// Where an IF's branches meet again, on the axis at the bottom of its extent.
export interface FlowJoin extends Point {
  id: string;
}

// What a wire connects, so a run can draw the path it took.
export interface WireRoute {
  // The nodes before and after the wire, null at a port, a join or a frame's edge.
  from: string | null;
  to: string | null;
  // Set on an IF's wires that no node of the branch decides: its curves, the run to the join, an empty branch.
  branch: { id: string; name: "then" | "else" } | null;
}

// A line; a curve that leaves start and reaches end going down, as a cubic whose control points sit halfway down
// above each end; or a repeat's wire back, a polyline with rounded corners.
export type FlowWire =
  | ({ shape: "line"; start: Point; end: Point } & WireRoute)
  | ({ shape: "bend"; start: Point; end: Point } & WireRoute)
  | ({ shape: "loop"; points: Point[]; radius: number } & WireRoute);

// An arrowhead pointing down, by its tip.
export interface FlowArrow extends Point {
  to: string;
  branch: WireRoute["branch"];
}

// A gap on a wire where nodes can go, by its middle.
export interface FlowSlot extends Point {
  slot: Slot;
}

export interface FlowLayout {
  width: number;
  height: number;
  boxes: FlowBox[];
  frames: FlowFrame[];
  ports: FlowPort[];
  joins: FlowJoin[];
  wires: FlowWire[];
  arrows: FlowArrow[];
  slots: FlowSlot[];
}
