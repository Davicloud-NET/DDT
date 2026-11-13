// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// The class ids from wuapi.idl and Microsoft Update's service id.
internal static class WindowsUpdateIds
{
    public static readonly Guid UpdateSession = new("4CB43D7F-7EEE-4906-8698-60DA1C38F2FE");

    public static readonly Guid UpdateCollection = new("13639463-00DB-4646-803D-528026140D88");

    public static readonly Guid UpdateServiceManager = new("F8D253D9-89A4-4DAA-87B6-1168369F0B21");

    public static readonly Guid SystemInformation = new("C01B9BA0-BEA7-41BA-B604-D0A36F469133");

    public const string MicrosoftUpdate = "7971f918-a847-4430-9279-4a52d1efe18d";

    public const string ClientApplication = "DDT";

    public const string Criteria = "IsInstalled=0 and IsHidden=0";

    // asfAllowPendingRegistration, asfAllowOnlineRegistration and asfRegisterServiceWithAU.
    public const int RegistrationFlags = 7;

    // ServerSelection's ssOthers, which searches the service ServiceID names.
    public const int OtherServer = 3;

    // IUpdateServiceRegistration.RegistrationState's usrsRegistered.
    public const int Registered = 3;
}
