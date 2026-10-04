// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDT.Server.Data;

// "Approved by" and the like, which name a user and outlive the account. PostgreSQL and SQLite set them to null when
// the user goes. SQL Server can't, so DDT clears them there before it deletes the user.
internal static class UserReferences
{
    // One transaction, so a delete that fails leaves the references as they were
    public static Task<IdentityResult> DeleteAsync(DdtDbContext database, UserManager<DdtUser> users, DdtUser user, CancellationToken cancellationToken)
    {
        if (!Keys(database).Any())
        {
            return users.DeleteAsync(user);
        }

        return database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await ClearAsync(database, user.Id, cancellationToken).ConfigureAwait(false);
                IdentityResult deleted = await users.DeleteAsync(user).ConfigureAwait(false);

                if (deleted.Succeeded)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                return deleted;
            }
        });
    }

    private static async Task ClearAsync(DdtDbContext database, Guid userId, CancellationToken cancellationToken)
    {
        ISqlGenerationHelper sql = database.GetService<ISqlGenerationHelper>();

        foreach (IForeignKey key in Keys(database))
        {
            IEntityType entity = key.DeclaringEntityType;

            if (entity.GetTableName() is not { } name)
            {
                continue;
            }

            string table = sql.DelimitIdentifier(name, entity.GetSchema());
            string column = sql.DelimitIdentifier(key.Properties[0].GetColumnName());
            string update = $"UPDATE {table} SET {column} = NULL WHERE {column} = {{0}}";

            // The names come from the model, and the user's id is a parameter
            await database.Database.ExecuteSqlRawAsync(update, [userId], cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<IForeignKey> Keys(DdtDbContext database) =>
        database.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(key => key.PrincipalEntityType.ClrType == typeof(DdtUser)
                && key.DeleteBehavior == DeleteBehavior.ClientSetNull
                && key.Properties is [{ IsNullable: true }]);
}
