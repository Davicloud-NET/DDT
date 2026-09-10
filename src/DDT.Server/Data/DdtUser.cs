using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Data;

public sealed class DdtUser : IdentityUser<Guid>
{
    public AccountSource Source { get; set; } = AccountSource.Local;

    public string? DirectoryObjectId { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? LastSignInUtc { get; set; }

    public bool IsDisabled { get; set; }
}
