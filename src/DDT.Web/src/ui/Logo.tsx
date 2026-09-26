// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The Davicloud mark: a red sphere with a swept orbital ring. The ring takes the logo-ring token, so it stays
// visible on the dark frame and on light pages alike.
export function Logo({ size = 24, className }: { size?: number; className?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" className={className}>
      <g transform="rotate(-22 16 16)">
        <path
          d="M1 16a15 5.25 0 0 1 30 0"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
          strokeLinecap="round"
        />
        <circle cx="16" cy="16" r="10.5" fill="#E0484A" />
        <path
          d="M1 16a15 5.25 0 0 0 30 0"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
          strokeLinecap="round"
        />
      </g>
    </svg>
  );
}
