// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Principal;
using DDT.Agent.WindowsPhase;

namespace DDT.Agent.Tests;

// Accounts, the auto-logon secret, profiles and sessions in memory, with every change noted in order.
internal sealed class FakeSessionAccounts : ISessionAccounts
{
    public static readonly SecurityIdentifier AccountSid = new("S-1-5-21-1111111111-2222222222-3333333333-1005");

    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly Dictionary<string, string> _passwords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _sessions = [];

    public string ProfilePath { get; set; } = @"C:\Users\DDTDeploy";

    public bool HasProfile { get; private set; }

    public string? StoredAutoLogonPassword { get; private set; }

    // How many more times DeleteProfile finds the profile in use.
    public int ProfileInUse { get; set; }

    public List<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public string? PasswordOf(string name)
    {
        lock (_lock)
        {
            return _passwords.GetValueOrDefault(name);
        }
    }

    public bool IsDisabled(string name)
    {
        lock (_lock)
        {
            return _disabled.Contains(name);
        }
    }

    public void SignIn(int session)
    {
        lock (_lock)
        {
            _sessions.Add(session);
        }
    }

    // Someone at the machine signs out.
    public void SignedOutByHand()
    {
        lock (_lock)
        {
            _sessions.Clear();
        }
    }

    public bool Create(string name, string password)
    {
        lock (_lock)
        {
            if (!_passwords.TryAdd(name, password))
            {
                return false;
            }

            _calls.Add($"create {name}");

            return true;
        }
    }

    public SecurityIdentifier Sid(string name) =>
        Exists(name) ? AccountSid : throw new IdentityNotMappedException($"No account {name}.");

    public bool Exists(string name)
    {
        lock (_lock)
        {
            return _passwords.ContainsKey(name);
        }
    }

    public void SetPassword(string name, string password)
    {
        lock (_lock)
        {
            _passwords[name] = password;
            _calls.Add($"set the password of {name}");
        }
    }

    public void Disable(string name, string password)
    {
        lock (_lock)
        {
            _passwords[name] = password;
            _disabled.Add(name);
            _calls.Add($"disable {name}");
        }
    }

    public bool Delete(string name)
    {
        lock (_lock)
        {
            _calls.Add($"delete {name}");

            return _passwords.Remove(name);
        }
    }

    public string? AutoLogonPassword()
    {
        lock (_lock)
        {
            return StoredAutoLogonPassword;
        }
    }

    public void SetAutoLogonPassword(string? password)
    {
        lock (_lock)
        {
            StoredAutoLogonPassword = password;
            _calls.Add(password is null ? "delete the auto-logon password" : "store the auto-logon password");
        }
    }

    public string CreateProfile(SecurityIdentifier sid, string name)
    {
        lock (_lock)
        {
            if (!HasProfile)
            {
                HasProfile = true;
                _calls.Add($"create the profile of {name}");
            }

            return ProfilePath;
        }
    }

    public bool DeleteProfile(SecurityIdentifier sid)
    {
        lock (_lock)
        {
            if (ProfileInUse > 0)
            {
                ProfileInUse--;

                throw new IOException("The profile is in use.");
            }

            _calls.Add("delete the profile");
            bool had = HasProfile;
            HasProfile = false;

            return had;
        }
    }

    public IReadOnlyList<int> Sessions(string name)
    {
        lock (_lock)
        {
            return [.. _sessions];
        }
    }

    public void SignOut(int sessionId)
    {
        lock (_lock)
        {
            _sessions.Remove(sessionId);
            _calls.Add($"sign out session {sessionId}");
        }
    }
}
