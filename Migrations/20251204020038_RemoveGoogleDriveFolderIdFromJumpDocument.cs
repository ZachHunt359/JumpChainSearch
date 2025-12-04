using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGoogleDriveFolderIdFromJumpDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migration removes GoogleDriveFolderId from JumpDocuments table
            // We need to handle FTS triggers and foreign keys carefully
            
            migrationBuilder.Sql(@"
                -- Step 1: Disable foreign key constraints
                PRAGMA foreign_keys = OFF;
                
                -- Step 2: Drop all FTS triggers that reference JumpDocuments
                DROP TRIGGER IF EXISTS JumpDocuments_ai;
                DROP TRIGGER IF EXISTS JumpDocuments_au;
                DROP TRIGGER IF EXISTS JumpDocuments_ad;
                DROP TRIGGER IF EXISTS DocumentTags_ai;
                DROP TRIGGER IF EXISTS DocumentTags_au;
                DROP TRIGGER IF EXISTS DocumentTags_ad;
                
                -- Step 3: Create new table without GoogleDriveFolderId
                CREATE TABLE JumpDocuments_new (
                    Id INTEGER NOT NULL CONSTRAINT PK_JumpDocuments PRIMARY KEY AUTOINCREMENT,
                    CreatedTime TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    DownloadLink TEXT NOT NULL,
                    ExtractedText TEXT NULL,
                    ExtractionMethod TEXT NULL,
                    FolderPath TEXT NOT NULL,
                    GoogleDriveFileId TEXT NOT NULL,
                    HasThumbnail INTEGER NOT NULL,
                    LastModified TEXT NOT NULL,
                    LastScanned TEXT NOT NULL,
                    MimeType TEXT NOT NULL,
                    ModifiedTime TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    OcrQuality REAL NULL,
                    Size INTEGER NOT NULL,
                    SourceDrive TEXT NOT NULL,
                    TextLastEditedAt TEXT NULL,
                    TextLastEditedBy TEXT NULL,
                    TextNeedsReview INTEGER NOT NULL,
                    TextReviewFlaggedAt TEXT NULL,
                    TextReviewFlaggedBy TEXT NULL,
                    ThumbnailLink TEXT NOT NULL,
                    WebViewLink TEXT NOT NULL
                );
                
                -- Step 4: Copy all data except GoogleDriveFolderId
                INSERT INTO JumpDocuments_new (
                    Id, CreatedTime, Description, DownloadLink, ExtractedText, ExtractionMethod,
                    FolderPath, GoogleDriveFileId, HasThumbnail, LastModified, LastScanned,
                    MimeType, ModifiedTime, Name, OcrQuality, Size, SourceDrive,
                    TextLastEditedAt, TextLastEditedBy, TextNeedsReview, TextReviewFlaggedAt,
                    TextReviewFlaggedBy, ThumbnailLink, WebViewLink
                )
                SELECT 
                    Id, CreatedTime, Description, DownloadLink, ExtractedText, ExtractionMethod,
                    FolderPath, GoogleDriveFileId, HasThumbnail, LastModified, LastScanned,
                    MimeType, ModifiedTime, Name, OcrQuality, Size, SourceDrive,
                    TextLastEditedAt, TextLastEditedBy, TextNeedsReview, TextReviewFlaggedAt,
                    TextReviewFlaggedBy, ThumbnailLink, WebViewLink
                FROM JumpDocuments;
                
                -- Step 5: Drop old table (safe because FK disabled)
                DROP TABLE JumpDocuments;
                
                -- Step 6: Rename new table
                ALTER TABLE JumpDocuments_new RENAME TO JumpDocuments;
                
                -- Step 7: Recreate indexes
                CREATE UNIQUE INDEX IX_JumpDocuments_GoogleDriveFileId ON JumpDocuments (GoogleDriveFileId);
                CREATE INDEX IX_JumpDocuments_FolderPath ON JumpDocuments (FolderPath);
                CREATE INDEX IX_JumpDocuments_Name ON JumpDocuments (Name);
                CREATE INDEX IX_JumpDocuments_SourceDrive_Name ON JumpDocuments (SourceDrive, Name);
                
                -- Step 8: Re-enable foreign key constraints
                PRAGMA foreign_keys = ON;
            ");
            
            // Step 9: Recreate FTS triggers (separate SQL to ensure table is ready)
            migrationBuilder.Sql(@"
                -- Recreate FTS triggers for JumpDocuments
                CREATE TRIGGER JumpDocuments_ai AFTER INSERT ON JumpDocuments BEGIN
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT new.Id, new.Name, new.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = new.Id),
                           new.ExtractedText;
                END;
                
                CREATE TRIGGER JumpDocuments_au AFTER UPDATE ON JumpDocuments BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 'delete', old.Id, old.Name, old.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = old.Id),
                           old.ExtractedText;
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT new.Id, new.Name, new.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = new.Id),
                           new.ExtractedText;
                END;
                
                CREATE TRIGGER JumpDocuments_ad AFTER DELETE ON JumpDocuments BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 'delete', old.Id, old.Name, old.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = old.Id),
                           old.ExtractedText;
                END;
                
                -- Recreate FTS triggers for DocumentTags
                CREATE TRIGGER DocumentTags_ai AFTER INSERT ON DocumentTags BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 'delete', d.Id, d.Name, d.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id),
                           d.ExtractedText
                    FROM JumpDocuments d WHERE d.Id = new.JumpDocumentId;
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT d.Id, d.Name, d.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id),
                           d.ExtractedText
                    FROM JumpDocuments d WHERE d.Id = new.JumpDocumentId;
                END;
                
                CREATE TRIGGER DocumentTags_au AFTER UPDATE ON DocumentTags BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 'delete', d.Id, d.Name, d.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id),
                           d.ExtractedText
                    FROM JumpDocuments d WHERE d.Id = old.JumpDocumentId;
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT d.Id, d.Name, d.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id),
                           d.ExtractedText
                    FROM JumpDocuments d WHERE d.Id = new.JumpDocumentId;
                END;
                
                CREATE TRIGGER DocumentTags_ad AFTER DELETE ON DocumentTags BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 'delete', d.Id, d.Name, d.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id),
                           d.ExtractedText
                    FROM JumpDocuments d WHERE d.Id = old.JumpDocumentId;
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT d.Id, d.Name, d.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id),
                           d.ExtractedText
                    FROM JumpDocuments d WHERE d.Id = old.JumpDocumentId;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback: Add the column back
            migrationBuilder.AddColumn<string>(
                name: "GoogleDriveFolderId",
                table: "JumpDocuments",
                type: "TEXT",
                nullable: true);
        }
    }
}
