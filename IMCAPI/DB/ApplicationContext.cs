using IMCAPI.DB.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.DB
{
    public class ApplicationContext : IdentityDbContext
    {
        public virtual DbSet<Item> Items { get; set; }

        public virtual DbSet<Attachment> Attachments { get; set; }

        public virtual DbSet<Constants> Constants { get; set; }
        public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
        public virtual DbSet<AuditLog> AuditLogs { get; set; }
        public virtual DbSet<UserProfile> UserProfiles { get; set; }

        public ApplicationContext(DbContextOptions<ApplicationContext> options)
            : base(options)
        {
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

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(log => log.Id);
                entity.HasIndex(log => new { log.Entity, log.EntityId });
                entity.HasIndex(log => log.TimestampUtc);
                entity.Property(log => log.Action).HasMaxLength(32).IsRequired();
                entity.Property(log => log.Entity).HasMaxLength(256);
                entity.Property(log => log.EntityId).HasMaxLength(512);
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
            modelBuilder.Entity<Item>().HasQueryFilter(i => i.IsActive);
            modelBuilder.Entity<Attachment>().HasQueryFilter(i => i.IsActive);
            modelBuilder.Entity<Constants>().HasQueryFilter(i => i.IsActive);

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
                entity.HasIndex(e => new { e.Group, e.Label }).IsUnique();
                entity.HasIndex(e => new { e.ParentId, e.Label }).IsUnique();
                entity.HasIndex(e => e.ParentId);
            });
        }

    }
}
