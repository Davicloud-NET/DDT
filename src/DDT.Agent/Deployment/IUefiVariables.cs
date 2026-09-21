// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The UEFI global variables, such as BootOrder and Boot####.
public interface IUefiVariables
{
    // Null when the variable does not exist.
    byte[]? Read(string name);

    void Write(string name, byte[] value);

    // Nothing happens when the variable does not exist.
    void Delete(string name);
}
