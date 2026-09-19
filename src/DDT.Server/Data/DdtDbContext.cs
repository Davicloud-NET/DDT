using DDT.Server.Machines;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Data;

public sealed class DdtDbContext(DbContextOptions<DdtDbContext> options)
    : IdentityDbContext<DdtUser, DdtRole, Guid>(options)
{
    public const string Schema = "ddt";

    public DbSet<Machine> Machines => Set<Machine>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<MachineLogLine> MachineLogLines => Set<MachineLogLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);

        builder.Entity<DdtUser>(user =>
        {
            user.Property(u => u.Source).HasConversion<string>().HasMaxLength(16);
            user.Property(u => u.DirectoryObjectId).HasMaxLength(256);
            user.Property(u => u.DisplayName).HasMaxLength(256);
            user.HasIndex(u => u.DirectoryObjectId).IsUnique().HasFilter(null);
        });

        builder.Entity<Machine>(machine =>
        {
            machine.Property(m => m.SmbiosUuid).HasMaxLength(64);
            machine.Property(m => m.PrimaryMac).HasMaxLength(32);
            machine.Property(m => m.MacAddresses).HasMaxLength(256);
            machine.Property(m => m.AgentVersion).HasMaxLength(32);
            machine.Property(m => m.LastSeenAddress).HasMaxLength(64);
            machine.Property(m => m.Manufacturer).HasMaxLength(128);
            machine.Property(m => m.Model).HasMaxLength(128);
            machine.Property(m => m.SerialNumber).HasMaxLength(128);
            machine.Property(m => m.AssignedName).HasMaxLength(15);
            machine.Property(m => m.FirstSeenAddress).HasMaxLength(64);
            machine.Property(m => m.State).HasConversion<string>().HasMaxLength(16);

            // State and generation are checked on save, so an approval, a rejection and a registration that
            // starts over cannot silently overwrite one another: the loser retries or reports a conflict.
            machine.Property(m => m.State).IsConcurrencyToken();
            machine.Property(m => m.TokenGeneration).IsConcurrencyToken();
            machine.HasIndex(m => m.SmbiosUuid);
            machine.HasIndex(m => m.PrimaryMac);
            machine.HasIndex(m => m.FirstSeenAddress);
            machine.HasOne(m => m.ApprovedBy).WithMany().HasForeignKey(m => m.ApprovedByUserId).OnDelete(DeleteBehavior.SetNull);
            machine.Property(m => m.SignedInUserName).HasMaxLength(256);
            machine.HasOne<DdtUser>().WithMany().HasForeignKey(m => m.SignedInByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<MachineLogLine>(line =>
        {
            line.Property(l => l.Level).HasConversion<string>().HasMaxLength(16);
            line.Property(l => l.Message).HasMaxLength(MachineLogLimits.MaxMessageLength);
            line.HasIndex(l => new { l.MachineId, l.Id });
            line.HasOne<Machine>().WithMany().HasForeignKey(l => l.MachineId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditEvent>(audit =>
        {
            audit.Property(a => a.Action).HasMaxLength(64);
            audit.Property(a => a.ActorName).HasMaxLength(256);
            audit.Property(a => a.SubjectId).HasMaxLength(64);
            audit.Property(a => a.SourceAddress).HasMaxLength(64);
            audit.Property(a => a.Detail).HasMaxLength(2048);
            audit.HasIndex(a => a.OccurredUtc);
            audit.HasIndex(a => a.Action);
        });
    }
}
