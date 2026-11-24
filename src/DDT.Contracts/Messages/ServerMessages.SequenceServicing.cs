// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // The Service packages step.

    public static readonly MessageTemplate SequenceServicingChoose = Define(
        "sequence.servicingChoose",
        "Add a Windows package, or turn on the matching updates.");

    public static readonly MessageTemplate SequenceServicingTooMany = Define(
        "sequence.servicingTooMany",
        "A step adds at most {max} Windows packages.");

    public static readonly MessageTemplate SequenceServicingRepeated = Define(
        "sequence.servicingRepeated",
        "This package is listed twice.");

    public static readonly MessageTemplate SequenceServicingBeforeImage = Define(
        "sequence.servicingBeforeImage",
        "Windows packages can be added only after the image is applied.");

    public static readonly MessageTemplate SequenceServicingPackageGone = Define(
        "sequence.servicingPackageGone",
        "The package is no longer in the library. Take it out of the list.");

    public static readonly MessageTemplate SequenceServicingNotWindows = Define(
        "sequence.servicingNotWindows",
        "{package} is not a Windows package.");
}
