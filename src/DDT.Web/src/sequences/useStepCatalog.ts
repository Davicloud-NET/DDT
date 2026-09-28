// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { deploymentOptionsQuery } from "@/deployments/deployments";
import { imagesQuery, type ImageSummary } from "@/images/images";
import type { liveListOptions } from "@/live/freshness";
import { packagesQuery, type PackageSummary } from "@/packages/packages";

// What the step fields choose from.
export interface StepCatalog {
  images: ImageSummary[];
  packages: PackageSummary[];
  // Null until the server's settings are read.
  domainConfigured: boolean | null;
}

export function useStepCatalog(freshness: ReturnType<typeof liveListOptions>): StepCatalog {
  const images = useQuery({ ...imagesQuery, ...freshness });
  const packages = useQuery({ ...packagesQuery, ...freshness });
  const options = useQuery(deploymentOptionsQuery);

  return {
    images: images.data ?? [],
    packages: packages.data ?? [],
    domainConfigured: options.data?.domainConfigured ?? null,
  };
}
