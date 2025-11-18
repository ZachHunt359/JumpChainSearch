using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Models;
using System.Text.Json;

namespace JumpChainSearch.Data
{
    public class JumpChainDbContext : DbContext
    {
        public JumpChainDbContext(DbContextOptions<JumpChainDbContext> options) : base(options)
        {
        }

        public DbSet<JumpDocument> JumpDocuments { get; set; }
        public DbSet<DocumentTag> DocumentTags { get; set; }
        public DbSet<DocumentUrl> DocumentUrls { get; set; }
        public DbSet<DocumentPurchasable> DocumentPurchasables { get; set; }
        public DbSet<DriveConfiguration> DriveConfigurations { get; set; }
        public DbSet<FolderConfiguration> FolderConfigurations { get; set; }
        
        // Tag voting system
        public DbSet<TagSuggestion> TagSuggestions { get; set; }
        public DbSet<TagRemovalRequest> TagRemovalRequests { get; set; }
        public DbSet<TagVote> TagVotes { get; set; }
        public DbSet<UserTagOverride> UserTagOverrides { get; set; }
        public DbSet<VotingConfiguration> VotingConfigurations { get; set; }
        public DbSet<DocumentViewCount> DocumentViewCounts { get; set; }
        public DbSet<ApprovedTagRule> ApprovedTagRules { get; set; }
        
        // Tag hierarchy
        public DbSet<TagHierarchy> TagHierarchies { get; set; }
        
        // Admin authentication
        public DbSet<AdminUser> AdminUsers { get; set; }
        public DbSet<AdminSession> AdminSessions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure JumpDocument
            modelBuilder.Entity<JumpDocument>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.GoogleDriveFileId).IsUnique();
                entity.HasIndex(e => e.Name);
                entity.HasIndex(e => new { e.SourceDrive, e.Name });
                entity.HasIndex(e => e.FolderPath); // NEW: Index for FolderPath searches
                
                entity.Property(e => e.Name).HasMaxLength(500);
                entity.Property(e => e.SourceDrive).HasMaxLength(200);
                entity.Property(e => e.MimeType).HasMaxLength(100);
                entity.Property(e => e.FolderPath).HasMaxLength(1000);
            });

            // Configure DocumentTag
            modelBuilder.Entity<DocumentTag>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.JumpDocumentId, e.TagName }).IsUnique();
                entity.HasIndex(e => e.TagName);
                entity.HasIndex(e => e.TagCategory);
                
                entity.Property(e => e.TagName).HasMaxLength(200);
                entity.Property(e => e.TagCategory).HasMaxLength(50);

                entity.HasOne(d => d.JumpDocument)
                    .WithMany(p => p.Tags)
                    .HasForeignKey(d => d.JumpDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure DocumentUrl
            modelBuilder.Entity<DocumentUrl>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.GoogleDriveFileId).IsUnique();
                entity.HasIndex(e => e.JumpDocumentId);
                entity.HasIndex(e => new { e.JumpDocumentId, e.SourceDrive });
                
                entity.Property(e => e.GoogleDriveFileId).HasMaxLength(100);
                entity.Property(e => e.SourceDrive).HasMaxLength(200);
                entity.Property(e => e.FolderPath).HasMaxLength(1000);
                entity.Property(e => e.WebViewLink).HasMaxLength(1000);
                entity.Property(e => e.DownloadLink).HasMaxLength(1000);

                entity.HasOne(d => d.JumpDocument)
                    .WithMany(p => p.Urls)
                    .HasForeignKey(d => d.JumpDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure DocumentPurchasable
            modelBuilder.Entity<DocumentPurchasable>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.JumpDocumentId);
                entity.HasIndex(e => e.Name);
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.PrimaryCost);
                entity.HasIndex(e => new { e.Category, e.Name });
                
                entity.Property(e => e.Name).HasMaxLength(500);
                entity.Property(e => e.Category).HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(5000);
                entity.Property(e => e.CostsJson).HasMaxLength(2000);

                entity.HasOne(d => d.JumpDocument)
                    .WithMany(p => p.Purchasables)
                    .HasForeignKey(d => d.JumpDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure DriveConfiguration
            modelBuilder.Entity<DriveConfiguration>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.DriveId).IsUnique();
                entity.HasIndex(e => e.DriveName);
                
                entity.Property(e => e.DriveId).HasMaxLength(100);
                entity.Property(e => e.DriveName).HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(500);
            });

            // Configure FolderConfiguration
            modelBuilder.Entity<FolderConfiguration>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.FolderId).IsUnique();
                entity.HasIndex(e => e.ParentDriveId);
                entity.HasIndex(e => new { e.ParentDriveId, e.FolderName });
                entity.HasIndex(e => e.FolderPath);
                
                entity.Property(e => e.FolderId).HasMaxLength(100);
                entity.Property(e => e.FolderName).HasMaxLength(200);
                entity.Property(e => e.FolderPath).HasMaxLength(1000);
                entity.Property(e => e.ResourceKey).HasMaxLength(200);
                entity.Property(e => e.PreferredAuthMethod).HasMaxLength(50);
                entity.Property(e => e.Description).HasMaxLength(500);

                entity.HasOne(f => f.ParentDrive)
                    .WithMany()
                    .HasForeignKey(f => f.ParentDriveId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure TagSuggestion
            modelBuilder.Entity<TagSuggestion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.JumpDocumentId);
                entity.HasIndex(e => new { e.JumpDocumentId, e.TagName, e.TagCategory });
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.SuggestedByUserId);
                
                entity.Property(e => e.TagName).HasMaxLength(200);
                entity.Property(e => e.TagCategory).HasMaxLength(50);
                entity.Property(e => e.Status).HasMaxLength(50);
                entity.Property(e => e.SuggestedByUserId).HasMaxLength(100);

                entity.HasOne(d => d.JumpDocument)
                    .WithMany()
                    .HasForeignKey(d => d.JumpDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure TagRemovalRequest
            modelBuilder.Entity<TagRemovalRequest>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.JumpDocumentId);
                entity.HasIndex(e => e.DocumentTagId);
                entity.HasIndex(e => new { e.JumpDocumentId, e.TagName, e.TagCategory });
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.RequestedByUserId);
                
                entity.Property(e => e.TagName).HasMaxLength(200);
                entity.Property(e => e.TagCategory).HasMaxLength(50);
                entity.Property(e => e.Status).HasMaxLength(50);
                entity.Property(e => e.RequestedByUserId).HasMaxLength(100);

                entity.HasOne(d => d.JumpDocument)
                    .WithMany()
                    .HasForeignKey(d => d.JumpDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.DocumentTag)
                    .WithMany()
                    .HasForeignKey(d => d.DocumentTagId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Configure TagVote
            modelBuilder.Entity<TagVote>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.TagSuggestionId }).IsUnique();
                entity.HasIndex(e => new { e.UserId, e.TagRemovalRequestId }).IsUnique();
                entity.HasIndex(e => e.TagSuggestionId);
                entity.HasIndex(e => e.TagRemovalRequestId);
                
                entity.Property(e => e.UserId).HasMaxLength(100);

                entity.HasOne(d => d.TagSuggestion)
                    .WithMany(p => p.Votes)
                    .HasForeignKey(d => d.TagSuggestionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.TagRemovalRequest)
                    .WithMany(p => p.Votes)
                    .HasForeignKey(d => d.TagRemovalRequestId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure UserTagOverride
            modelBuilder.Entity<UserTagOverride>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.JumpDocumentId, e.TagName, e.TagCategory }).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.JumpDocumentId);
                
                entity.Property(e => e.UserId).HasMaxLength(100);
                entity.Property(e => e.TagName).HasMaxLength(200);
                entity.Property(e => e.TagCategory).HasMaxLength(50);

                entity.HasOne(d => d.JumpDocument)
                    .WithMany()
                    .HasForeignKey(d => d.JumpDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure VotingConfiguration
            modelBuilder.Entity<VotingConfiguration>(entity =>
            {
                entity.HasKey(e => e.Id);
                
                entity.Property(e => e.ModifiedBy).HasMaxLength(100);
            });

            // Configure DocumentViewCount
            modelBuilder.Entity<DocumentViewCount>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.JumpDocumentId).IsUnique();

                entity.HasOne(d => d.JumpDocument)
                    .WithMany()
                    .HasForeignKey(d => d.JumpDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure ApprovedTagRule
            modelBuilder.Entity<ApprovedTagRule>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.GoogleDriveFileId);
                entity.HasIndex(e => new { e.GoogleDriveFileId, e.TagName, e.TagCategory, e.RuleType });
                entity.HasIndex(e => e.RuleType);
                entity.HasIndex(e => e.ApprovalSource);
                entity.HasIndex(e => e.IsActive);
                
                entity.Property(e => e.GoogleDriveFileId).HasMaxLength(100).IsRequired();
                entity.Property(e => e.DocumentName).HasMaxLength(500).IsRequired();
                entity.Property(e => e.TagName).HasMaxLength(200).IsRequired();
                entity.Property(e => e.TagCategory).HasMaxLength(50).IsRequired();
                entity.Property(e => e.RuleType).HasMaxLength(20).IsRequired();
                entity.Property(e => e.ApprovalSource).HasMaxLength(50).IsRequired();
                entity.Property(e => e.ApprovedByUserId).HasMaxLength(100);
            });
            
            // Configure TagHierarchy
            modelBuilder.Entity<TagHierarchy>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.ParentTagName, e.ChildTagName }).IsUnique();
                entity.HasIndex(e => e.ParentTagName);
                entity.HasIndex(e => e.ChildTagName);
                
                entity.Property(e => e.ParentTagName).HasMaxLength(200);
                entity.Property(e => e.ChildTagName).HasMaxLength(200);
            });

            // Configure TagVote
            modelBuilder.Entity<TagVote>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.TagSuggestionId);
                entity.HasIndex(e => e.TagRemovalRequestId);
                entity.HasIndex(e => e.UserId);
                
                entity.Property(e => e.UserId).HasMaxLength(100);

                entity.HasOne(d => d.TagSuggestion)
                    .WithMany()
                    .HasForeignKey(d => d.TagSuggestionId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(d => d.TagRemovalRequest)
                    .WithMany()
                    .HasForeignKey(d => d.TagRemovalRequestId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Configure AdminUser
            modelBuilder.Entity<AdminUser>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
                
                entity.Property(e => e.Username).HasMaxLength(50);
                entity.Property(e => e.PasswordHash).IsRequired();
                entity.Property(e => e.Salt).IsRequired();
            });

            // Configure AdminSession
            modelBuilder.Entity<AdminSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SessionToken).IsUnique();
                entity.HasIndex(e => e.ExpiresAt);
                
                entity.Property(e => e.SessionToken).IsRequired();
                entity.Property(e => e.IpAddress).HasMaxLength(45); // IPv6 max length
                entity.Property(e => e.UserAgent).HasMaxLength(500);

                entity.HasOne(d => d.AdminUser)
                    .WithMany()
                    .HasForeignKey(d => d.AdminUserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}