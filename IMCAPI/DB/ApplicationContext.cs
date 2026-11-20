using IMCAPI.DB.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using IMCAPI.Services;

namespace IMCAPI.DB
{
    public class ApplicationContext : IdentityDbContext
    {
        private readonly TenantScope _tenant;
        public int CurrentTenantId => _tenant.TenantId;

        /// <summary>The acting caller is allowed to touch the shared (tenant 0) rows.</summary>
        public bool IsSuperadmin => _tenant.IsSuperadmin;
        public virtual DbSet<Item> Items { get; set; }

        public virtual DbSet<Attachment> Attachments { get; set; }

        public virtual DbSet<Constants> Constants { get; set; }
        public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
        public virtual DbSet<AuditLog> AuditLogs { get; set; }
        public virtual DbSet<UserProfile> UserProfiles { get; set; }
        public virtual DbSet<ItemArchive> ItemArchives { get; set; }

        public virtual DbSet<ImpersonationSession> ImpersonationSessions { get; set; }

        public ApplicationContext(DbContextOptions<ApplicationContext> options, TenantScope tenant)
            : base(options)
        {
            _tenant = tenant;
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.EnableSensitiveDataLogging();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasIndex(token => token.TokenHash).IsUnique();
                entity.HasIndex(token => token.UserId);
                entity.HasIndex(token => new { token.TenantId, token.UserId });
                entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
                entity.Property(token => token.UserId).IsRequired();
                entity.HasOne<IdentityUser>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Item>().ToTable("Items");
            modelBuilder.Entity<Attachment>().ToTable("Attachments");
            modelBuilder.Entity<Constants>().ToTable("Constants");

            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.ToTable("UserProfiles");
                entity.HasKey(profile => profile.UserId);
                entity.Property(profile => profile.UserId).HasMaxLength(450);
                entity.HasOne<IdentityUser>().WithOne().HasForeignKey<UserProfile>(profile => profile.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ImpersonationSession>(entity =>
            {
                entity.HasKey(session => session.Id);
                entity.HasIndex(session => session.ActorUserId);
                entity.HasIndex(session => new { session.TenantId, session.ActorUserId });
                entity.HasQueryFilter(session => session.TenantId == CurrentTenantId);
                entity.Property(session => session.Id).HasMaxLength(64);
                entity.Property(session => session.ActorUserId).HasMaxLength(64);
                entity.Property(session => session.ActorEmail).HasMaxLength(320);
                entity.Property(session => session.TargetRole).HasMaxLength(32);
                entity.Property(session => session.EndedBy).HasMaxLength(64);
            });

            modelBuilder.Entity<ItemArchive>(entity =>
            {
                entity.ToTable("ItemsArchive");
                entity.HasKey(archive => archive.ArchiveId);
                entity.Property(archive => archive.ArchiveId).ValueGeneratedOnAdd();
                entity.Property(archive => archive.Label).HasMaxLength(1000).IsRequired();
                entity.Property(archive => archive.Icon).HasMaxLength(50).IsRequired();
                entity.Property(archive => archive.CreatedBy).HasMaxLength(320);
                entity.Property(archive => archive.UpdatedBy).HasMaxLength(320);
                entity.Property(archive => archive.DeletedById).HasMaxLength(450);
                entity.Property(archive => archive.DeletedBy).HasMaxLength(320);
                entity.Property(archive => archive.RestoredById).HasMaxLength(450);
                entity.HasIndex(archive => archive.ItemId);
                entity.HasIndex(archive => new { archive.TenantId, archive.ItemId });
                entity.HasQueryFilter(archive => archive.TenantId == CurrentTenantId);
                entity.HasIndex(archive => archive.DeletedAtUtc);
                entity.HasIndex(archive => archive.DeletedById);
                // The archive screen filters by label; without this the filter is a scan on a table
                // that only grows.
                entity.HasIndex(archive => archive.Label);
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(log => log.Id);
                entity.HasIndex(log => new { log.Entity, log.EntityId });
                entity.HasIndex(log => new { log.TenantId, log.TimestampUtc });
                entity.HasQueryFilter(log => log.TenantId == CurrentTenantId);
                entity.HasIndex(log => log.TimestampUtc);
                // Room for the action names this app will keep adding: SQLite does not enforce the
                // length, SQL Server does, and a 33-character action would fail only after a migration.
                entity.Property(log => log.Action).HasMaxLength(64).IsRequired();
                entity.Property(log => log.Entity).HasMaxLength(256);
                entity.Property(log => log.EntityId).HasMaxLength(1024);
                entity.Property(log => log.ActorUserId).HasMaxLength(450);
                entity.Property(log => log.ActorEmail).HasMaxLength(320);
                entity.Property(log => log.ImpersonatedUserId).HasMaxLength(450);
                entity.Property(log => log.IpAddress).HasMaxLength(64);
                entity.Property(log => log.UserAgent).HasMaxLength(2048);
                entity.Property(log => log.RequestId).HasMaxLength(128);
            });

            ConfigureItem(modelBuilder);

            ConfigureAttachment(modelBuilder);

            ConfigureConstant(modelBuilder);

            // ─────────────────────────────────────────────
            // GLOBAL QUERY FILTER (soft delete)
            // ─────────────────────────────────────────────
            modelBuilder.Entity<Item>().HasQueryFilter(i => i.IsActive && i.TenantId == CurrentTenantId);
            modelBuilder.Entity<Attachment>().HasQueryFilter(i => i.IsActive && i.TenantId == CurrentTenantId);
            modelBuilder.Entity<Constants>().HasQueryFilter(i => i.IsActive && (i.TenantId == CurrentTenantId || i.TenantId == 0));

        }


        private void ConfigureItem(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>(entity =>
            {
                // ── Properties ──
                entity.Property(e => e.Label)
                      .HasMaxLength(1000)
                      .IsRequired();
                entity.Property(e => e.Icon)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.Attributes);
//                      .HasColumnType("nvarchar(max)"); // JSON storage

                // ── Self-referencing relationship (Parent/Children) ──
                entity.HasOne(i => i.Parent)
                      .WithMany(i => i.Children)
                      .HasForeignKey(i => i.ParentId)
                      .OnDelete(DeleteBehavior.Restrict); // Prevent cascade on self-ref

                // ── Indexes ──
                entity.HasIndex(e => e.ParentId);
                entity.HasIndex(e => new { e.TenantId, e.ParentId });
            });
        }

        private void ConfigureAttachment(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Attachment>(entity =>
            {
                entity.Property(e => e.Type)
                      .HasMaxLength(200)
                      .IsRequired();

                entity.Property(e => e.FileName)
                      .HasMaxLength(500)
                      .IsRequired();

                entity.Property(e => e.FilePath)
                      .HasMaxLength(2000);

                entity.Property(e => e.ThumbnailBase64);
                //                      .HasColumnType("varbinary(max)");

                entity.Property(e => e.Attributes);
//                      .HasColumnType("nvarchar(max)");

                // ── Relationship to Item ──
                entity.HasOne<Item>()
                      .WithMany(i => i.Attachments)
                      .HasForeignKey(t => t.ItemId)
                      .OnDelete(DeleteBehavior.Cascade); // If item deleted, attachments go too

                // ── Indexes ──
                entity.HasIndex(e => e.ItemId);
                entity.HasIndex(e => new { e.TenantId, e.ItemId });
                entity.HasIndex(e => e.Type);
            });
        }

        private void ConfigureConstant(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Constants>(entity =>
            {
                // ── Properties ──
                entity.Property(e => e.Group)
                      .HasMaxLength(500)
                      .IsRequired();

                entity.Property(e => e.Label)
                      .HasMaxLength(1000)
                      .IsRequired();

                // The identity side of the code/label contract: the label is display text, the code
                // is what stored data and the API key on. Unique per (TenantId, Group), the same
                // boundary the label's own uniqueness uses.
                entity.Property(e => e.Code)
                      .HasMaxLength(1000)
                      .IsRequired();
                entity.Property(e => e.Icon)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.Description)
                      .HasMaxLength(5000);

                // ── Self-referencing (optional hierarchy) ──
                entity.HasOne(c => c.Parent)
                      .WithMany(c => c.Children)
                      .HasForeignKey(c => c.ParentId)
                      .OnDelete(DeleteBehavior.Restrict);

                // ── Indexes ──
                entity.HasIndex(e => e.Group);
                entity.HasIndex(e => new { e.TenantId, e.Group, e.Label }).IsUnique();
                entity.HasIndex(e => new { e.TenantId, e.Group, e.Code }).IsUnique()
                    // Partial on purpose: seed.sql and CopySql write rows before the backfill has
                    // given them their codes, and a plain unique index would reject the whole seed
                    // on the empty strings. Every row ends up with a code — the backfill, the
                    // controller's generation and the PUT preservation guarantee it — so among real
                    // codes the uniqueness the contract asks for holds.
                    .HasFilter("\"Code\" != ''");
                entity.HasIndex(e => new { e.ParentId, e.Label }).IsUnique();
                entity.HasIndex(e => e.ParentId);
            });
        }

    }
}
