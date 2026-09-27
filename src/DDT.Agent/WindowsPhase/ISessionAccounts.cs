// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Principal;

namespace DDT.Agent.WindowsPhase;

// What DDT's session needs of the Windows it runs in: a local account, the password Windows signs in with, the
// account's profile, and the sessions it is signed in to.
public interface ISessionAccounts
{
    // Creates a local standard account with that password, a member of Users; false when it exists already, whose
    // password stays as it was.
    bool Create(string name, string password);

    SecurityIdentifier Sid(string name);

    bool Exists(string name);

    void SetPassword(string name, string password);

    // Disables the account and gives it a password nobody knows, so nobody can sign in with it even if it stays.
    void Disable(string name, string password);

    // False when there was no such account.
    bool Delete(string name);

    // The auto-logon password Windows keeps as an LSA secret, or null when there is none.
    string? AutoLogonPassword();

    // Null deletes it.
    void SetAutoLogonPassword(string? password);

    // The account's profile, created as a new one would be at its first sign-in, or the one it has. Returns its path.
    string CreateProfile(SecurityIdentifier sid, string name);

    // False when the account has no profile. Throws when the profile is still in use.
    bool DeleteProfile(SecurityIdentifier sid);

    // The sessions the account is signed in to.
    IReadOnlyList<int> Sessions(string name);

    // Signs the session out and returns once it has ended.
    void SignOut(int sessionId);
}
