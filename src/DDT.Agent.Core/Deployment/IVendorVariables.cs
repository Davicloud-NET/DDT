// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// UEFI variables under a vendor's own GUID, which outlast a restart. Each member fails with a DeploymentStepException.
public interface IVendorVariables
{
    // Null when the variable does not exist.
    byte[]? Read(string name, Guid vendor);

    // Non-volatile, with boot service and runtime access, so the next boot environment reads it too. Linux creates
    // such a variable immutable, so its implementation makes it mutable before it writes or deletes.
    void Write(string name, Guid vendor, byte[] value);

    // Nothing happens when the variable does not exist.
    void Delete(string name, Guid vendor);
}
