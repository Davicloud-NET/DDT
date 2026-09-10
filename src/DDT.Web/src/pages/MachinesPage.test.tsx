import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { MachinesPage } from "./MachinesPage";

describe("MachinesPage", () => {
  it("explains that no machine has registered yet", () => {
    render(<MachinesPage />);

    expect(screen.getByRole("heading", { level: 1, name: "Machines" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "No machines yet" })).toBeInTheDocument();
  });
});
