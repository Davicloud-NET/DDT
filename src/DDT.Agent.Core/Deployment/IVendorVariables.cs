// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// UEFI variables under a vendor's own GUID, which outlast a restart into another boot environment. A failure is a
// DeploymentStepException. Linux creates such a variable immutable, so its implementation makes the variable mutable
// before it rewrites or deletes it.
public interface IVendorVariables
{
    // Null when the variable does not exist.
    byte[]? Read(string name, Guid vendor);

    // Non-volatile, with boot service and runtime access, so the next boot environment reads it too.
    void Write(string name, Guid vendor, byte[] value);

    // Nothing happens when the variable does not exist.
    void Delete(string name, Guid vendor);
}
