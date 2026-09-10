using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Ldap;

public sealed partial class DirectorySignInService(
    ILdapAuthenticator authenticator,
    UserManager<DdtUser> userManager,
    SignInManager<DdtUser> signInManager,
    IOptions<LdapOptions> options,
    ILogger<DirectorySignInService> logger)
{
    private readonly LdapOptions _options = options.Value;

    public bool Enabled => _options.Enabled;

    public async Task<SignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return SignInResult.Failed;
        }

        DdtUser? existing = await userManager.FindByNameAsync(userName).ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.Source != AccountSource.Directory)
            {
                return SignInResult.Failed;
            }

            if (existing.IsDisabled)
            {
                return SignInResult.NotAllowed;
            }

            // DDT applies its own lockout before forwarding anything to the directory. Without
            // this brake, DDT is a convenient way to lock out arbitrary domain accounts.
            if (await userManager.IsLockedOutAsync(existing).ConfigureAwait(false))
            {
                return SignInResult.LockedOut;
            }
        }

        LdapIdentity? identity = await authenticator
            .AuthenticateAsync(userName, password, cancellationToken)
            .ConfigureAwait(false);

        if (identity is null)
        {
            if (existing is not null)
            {
                await userManager.AccessFailedAsync(existing).ConfigureAwait(false);
            }

            return SignInResult.Failed;
        }

        DdtUser user = await ReconcileAsync(identity, existing).ConfigureAwait(false);

        if (user.IsDisabled)
        {
            return SignInResult.NotAllowed;
        }

        await userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        await ApplyRolesAsync(user, identity).ConfigureAwait(false);
        await signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);

        LogDirectorySignIn(user.UserName ?? string.Empty, identity.ImmutableId);

        return SignInResult.Success;
    }

    // Keyed on the directory's immutable identifier, never on the user name or the distinguished
    // name: both of those change when someone is renamed or moved between organisational units.
    private async Task<DdtUser> ReconcileAsync(LdapIdentity identity, DdtUser? matchedByName)
    {
        DdtUser? user = await userManager.Users
            .FirstOrDefaultAsync(u => u.DirectoryObjectId == identity.ImmutableId)
            .ConfigureAwait(false)
            ?? matchedByName;

        if (user is null)
        {
            user = new DdtUser
            {
                UserName = identity.UserName,
                Email = identity.Email,
                DisplayName = identity.DisplayName,
                Source = AccountSource.Directory,
                DirectoryObjectId = identity.ImmutableId,
                CreatedUtc = DateTimeOffset.UtcNow,
            };

            await userManager.CreateAsync(user).ConfigureAwait(false);
            LogDirectoryUserCreated(user.UserName ?? string.Empty, identity.ImmutableId);

            return user;
        }

        user.UserName = identity.UserName;
        user.Email = identity.Email;
        user.DisplayName = identity.DisplayName;
        user.DirectoryObjectId = identity.ImmutableId;
        user.LastSignInUtc = DateTimeOffset.UtcNow;

        await userManager.UpdateAsync(user).ConfigureAwait(false);

        return user;
    }

    private async Task ApplyRolesAsync(DdtUser user, LdapIdentity identity)
    {
        if (_options.GroupRoleMap.Count == 0)
        {
            return;
        }

        HashSet<string> mapped = new(StringComparer.OrdinalIgnoreCase);

        foreach (string groupDn in identity.GroupDns)
        {
            if (_options.GroupRoleMap.TryGetValue(groupDn, out string? role))
            {
                mapped.Add(role);
            }
        }

        IList<string> current = await userManager.GetRolesAsync(user).ConfigureAwait(false);

        // The directory is authoritative, so a role removed there is removed here on next sign in.
        string[] toRemove = [.. current.Where(role => !mapped.Contains(role))];
        string[] toAdd = [.. mapped.Where(role => !current.Contains(role, StringComparer.OrdinalIgnoreCase))];

        if (toRemove.Length > 0)
        {
            await userManager.RemoveFromRolesAsync(user, toRemove).ConfigureAwait(false);
        }

        if (toAdd.Length > 0)
        {
            await userManager.AddToRolesAsync(user, toAdd).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Information, Message = "Directory sign in for {UserName} ({ImmutableId})")]
    private partial void LogDirectorySignIn(string userName, string immutableId);

    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "Created directory backed account {UserName} ({ImmutableId})")]
    private partial void LogDirectoryUserCreated(string userName, string immutableId);
}
