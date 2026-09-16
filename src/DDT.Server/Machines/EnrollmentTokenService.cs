using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Machines;

// A token is "ddt1.<id>.<secret>". Only a SHA-256 of the secret is stored, so the database alone
// cannot be turned into a working token, and the id names which token a machine registered with.
public sealed class EnrollmentTokenService(DdtDbContext database, TimeProvider timeProvider)
{
    private const string Prefix = "ddt1";
    private const int SecretLength = 32;

    public async Task<(EnrollmentToken Record, string Token)> CreateAsync(
        string name,
        TimeSpan validity,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(SecretLength);
        DateTimeOffset now = timeProvider.GetUtcNow();

        EnrollmentToken record = new()
        {
            Id = Guid.CreateVersion7(now),
            Name = name,
            SecretHash = SHA256.HashData(secret),
            CreatedUtc = now,
            ExpiresUtc = now + validity,
            CreatedByUserId = createdByUserId,
        };

        database.EnrollmentTokens.Add(record);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (record, $"{Prefix}.{record.Id:N}.{Base64Url.EncodeToString(secret)}");
    }

    public async Task<EnrollmentToken?> ValidateAsync(string? presented, CancellationToken cancellationToken)
    {
        if (!TryParse(presented, out Guid id, out byte[] secret))
        {
            return null;
        }

        EnrollmentToken? record = await database.EnrollmentTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(token => token.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (record is null
            || record.RevokedUtc is not null
            || record.ExpiresUtc <= timeProvider.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(record.SecretHash, SHA256.HashData(secret)))
        {
            return null;
        }

        return record;
    }

    private static bool TryParse(string? presented, out Guid id, out byte[] secret)
    {
        id = Guid.Empty;
        secret = [];

        string[] parts = presented?.Split('.') ?? [];

        if (parts.Length != 3
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[1], "N", out id))
        {
            return false;
        }

        byte[] decoded = new byte[SecretLength];

        if (Base64Url.DecodeFromChars(parts[2], decoded, out _, out int written) != System.Buffers.OperationStatus.Done
            || written != SecretLength)
        {
            return false;
        }

        secret = decoded;

        return true;
    }
}
