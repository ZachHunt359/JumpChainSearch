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
            // Use raw SQL to handle everything manually to avoid trigger issues
            migrationBuilder.Sql(@"
                -- Drop FTS triggers
                DROP TRIGGER IF EXISTS JumpDocuments_ai;
                DROP TRIGGER IF EXISTS JumpDocuments_au;
                DROP TRIGGER IF EXISTS JumpDocuments_ad;
                DROP TRIGGER IF EXISTS DocumentTags_ai;
                DROP TRIGGER IF EXISTS DocumentTags_au;
                DROP TRIGGER IF EXISTS DocumentTags_ad;
                
                -- Drop temp table if it exists (from failed migration)
                DROP TABLE IF EXISTS ef_temp_JumpDocuments;
                
                -- Create temp table without GoogleDriveFolderId
                CREATE TABLE ef_temp_JumpDocuments (
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
                
                -- Copy data (excluding GoogleDriveFolderId)
                INSERT INTO ef_temp_JumpDocuments 
                SELECT Id, CreatedTime, Description, DownloadLink, ExtractedText, ExtractionMethod,
                       FolderPath, GoogleDriveFileId, HasThumbnail, LastModified, LastScanned,
                       MimeType, ModifiedTime, Name, OcrQuality, Size, SourceDrive,
                       TextLastEditedAt, TextLastEditedBy, TextNeedsReview, TextReviewFlaggedAt,
                       TextReviewFlaggedBy, ThumbnailLink, WebViewLink
                FROM JumpDocuments;
                
                -- Drop old table
                PRAGMA foreign_keys = 0;
                DROP TABLE JumpDocuments;
                ALTER TABLE ef_temp_JumpDocuments RENAME TO JumpDocuments;
                PRAGMA foreign_keys = 1;
                
                -- Recreate indexes
                CREATE UNIQUE INDEX IX_JumpDocuments_GoogleDriveFileId ON JumpDocuments (GoogleDriveFileId);
                CREATE INDEX IX_JumpDocuments_FolderPath ON JumpDocuments (FolderPath);
                CREATE INDEX IX_JumpDocuments_Name ON JumpDocuments (Name);
                CREATE INDEX IX_JumpDocuments_SourceDrive_Name ON JumpDocuments (SourceDrive, Name);
            ");
            
            // Recreate FTS triggers AFTER table is ready
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS JumpDocuments_ai AFTER INSERT ON JumpDocuments BEGIN
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT new.Id, new.Name, new.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = new.Id),
                           new.ExtractedText;
                END;
                
                CREATE TRIGGER IF NOT EXISTS JumpDocuments_au AFTER UPDATE ON JumpDocuments BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 'delete', old.Id, old.Name, old.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = old.Id),
                           old.ExtractedText;
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT new.Id, new.Name, new.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = new.Id),
                           new.ExtractedText;
                END;
                
                CREATE TRIGGER IF NOT EXISTS JumpDocuments_ad AFTER DELETE ON JumpDocuments BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 'delete', old.Id, old.Name, old.FolderPath,
                           (SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = old.Id),
                           old.ExtractedText;
                END;
                
                CREATE TRIGGER IF NOT EXISTS DocumentTags_ai AFTER INSERT ON DocumentTags BEGIN
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
                
                CREATE TRIGGER IF NOT EXISTS DocumentTags_au AFTER UPDATE ON DocumentTags BEGIN
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
                
                CREATE TRIGGER IF NOT EXISTS DocumentTags_ad AFTER DELETE ON DocumentTags BEGIN
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
            // Rollback not fully implemented - would need to add column back
            migrationBuilder.AddColumn<string>(
                name: "GoogleDriveFolderId",
                table: "JumpDocuments",
                type: "TEXT",
                nullable: true);
        }
    }
}
