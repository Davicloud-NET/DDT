// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId } from "react";

// DDT's mark: a D whose inner edge points forward, drawn from src/DDT.Design/brand/ddt-logo.svg and cropped to the
// mark. It carries its own reds, so it reads the same on the dark frame and on light pages. size is its height; the
// mark is a little wider than tall. The gradients get ids of their own, so two marks on a page do not share them.
export function Logo({ size = 24, className }: { size?: number; className?: string }) {
  const id = useId();
  const ref = (name: string) => `${id}-${name}`;
  const url = (name: string) => `url(#${ref(name)})`;

  return (
    <svg
      height={size}
      width={Math.round((size * 136) / 118)}
      viewBox="11 21 136 118"
      aria-hidden="true"
      className={className}
    >
      <defs>
        <clipPath id={ref("mark")}>
          <path d="M18.378,21.5h130.344v117H18.378c-3.919,0-7.1-3.181-7.1-7.1V28.6c0-3.919,3.181-7.1,7.1-7.1Z" />
        </clipPath>
        <linearGradient
          id={ref("d")}
          x1="27.399"
          y1="80"
          x2="146.306"
          y2="80"
          gradientUnits="userSpaceOnUse"
        >
          <stop offset="0" stopColor="#8b0f22" />
          <stop offset="1" stopColor="#ff5a5a" />
        </linearGradient>
        <clipPath id={ref("inner")}>
          <path d="M87.806,21.5H26.962l32.944,30.862,19.471-.067c2.329-.008,4.564.92,6.203,2.575l24.773,25.024c1.019,1.029,1.021,2.686.005,3.718l-24.787,25.18c-1.633,1.659-3.863,2.593-6.191,2.593l-15.286-.002-28.933,27.104,52.646.014c32.309,0,58.5-26.191,58.5-58.5s-26.191-58.5-58.5-58.5Z" />
        </clipPath>
        <linearGradient
          id={ref("band")}
          x1="-61.5"
          y1="79.721"
          x2="142.286"
          y2="79.721"
          gradientUnits="userSpaceOnUse"
        >
          <stop offset="0" stopColor="#ff5a5a" />
          <stop offset="1" stopColor="#8b0f22" />
        </linearGradient>
        <linearGradient
          id={ref("front")}
          x1="22.3"
          y1="157.58"
          x2="22.3"
          y2="58.177"
          gradientUnits="userSpaceOnUse"
        >
          <stop offset=".038" stopColor="#8b0f22" />
          <stop offset=".731" stopColor="#ff5a5a" />
          <stop offset=".787" stopColor="#d73a3a" />
        </linearGradient>
        <linearGradient
          id={ref("back")}
          x1="5.166"
          y1="107.878"
          x2="89.457"
          y2="107.878"
          gradientUnits="userSpaceOnUse"
        >
          <stop offset="0" stopColor="#480f22" />
          <stop offset="1" stopColor="#8b0f22" />
        </linearGradient>
      </defs>
      <g clipPath={url("mark")}>
        <path
          d="M18.378,21.5h48.12v30.839H18.378c-3.919,0-7.1-3.181-7.1-7.1v-16.639c0-3.919,3.181-7.1,7.1-7.1Z"
          fill="#8b0f22"
        />
        <path
          d="M87.806,21.5l-60.406.409,32.507,30.453,19.471-.067c2.329-.008,4.564.92,6.203,2.575l24.773,25.024c1.019,1.029,1.021,2.686.005,3.718l-24.787,25.18c-1.633,1.659-3.863,2.593-6.191,2.593l-15.286-.002-28.933,27.104,52.646.014c32.309,0,58.5-26.191,58.5-58.5s-26.191-58.5-58.5-58.5Z"
          fill={url("d")}
        />
        <g clipPath={url("inner")}>
          <path
            d="M138.664,71.095l-38.514-38.604c-7.025-7.041-16.573-10.983-26.519-10.949l-135.13-.596v117.536s135.141.018,135.141.018c9.94.001,19.47-3.967,26.47-11.024l38.577-38.887c4.806-4.845,4.795-12.662-.025-17.493Z"
            fill={url("band")}
          />
        </g>
        <path
          d="M41.348,58.177h-16.684c-.83,0-1.232,1.016-.626,1.584l20.543,19.245c1.154,1.081,1.153,2.911,0,3.992l-64.426,60.353v11.489c0,2.394,2.858,3.632,4.605,1.996L63.58,82.997c1.154-1.081,1.154-2.911,0-3.992l-22.233-20.828Z"
          fill={url("front")}
        />
        <path
          d="M88.592,79.005l-22.233-20.828h-25.012l22.233,20.828c1.154,1.081,1.153,2.911,0,3.992L5.167,137.718v17.121c0,2.394,2.858,3.632,4.604,1.996l78.821-73.839c1.154-1.081,1.154-2.911,0-3.992Z"
          fill={url("back")}
        />
      </g>
    </svg>
  );
}
