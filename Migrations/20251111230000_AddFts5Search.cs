using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpChainSearch.Migrations
{
    /// <inheritdoc />
    public partial class AddFts5Search : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create FTS5 virtual table for full-text search
            // Columns: Name, FolderPath, Tags (concatenated), ExtractedText
            migrationBuilder.Sql(@"
                CREATE VIRTUAL TABLE JumpDocuments_fts USING fts5(
                    Name,
                    FolderPath,
                    Tags,
                    ExtractedText,
                    content='',
                    contentless_delete=1
                );
            ");

            // Trigger: Insert - add to FTS5 when new document is inserted
            migrationBuilder.Sql(@"
                CREATE TRIGGER JumpDocuments_ai AFTER INSERT ON JumpDocuments BEGIN
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        new.Id,
                        new.Name,
                        new.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = new.Id), ''),
                        COALESCE(new.ExtractedText, '');
                END;
            ");

            // Trigger: Update - update FTS5 when document is modified
            migrationBuilder.Sql(@"
                CREATE TRIGGER JumpDocuments_au AFTER UPDATE ON JumpDocuments BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        'delete',
                        old.Id,
                        old.Name,
                        old.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = old.Id), ''),
                        COALESCE(old.ExtractedText, '');
                    
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        new.Id,
                        new.Name,
                        new.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = new.Id), ''),
                        COALESCE(new.ExtractedText, '');
                END;
            ");

            // Trigger: Delete - remove from FTS5 when document is deleted
            migrationBuilder.Sql(@"
                CREATE TRIGGER JumpDocuments_ad AFTER DELETE ON JumpDocuments BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        'delete',
                        old.Id,
                        old.Name,
                        old.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = old.Id), ''),
                        COALESCE(old.ExtractedText, '');
                END;
            ");

            // Trigger: Tag Insert - update FTS5 when tags are added
            migrationBuilder.Sql(@"
                CREATE TRIGGER DocumentTags_ai AFTER INSERT ON DocumentTags BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        'delete',
                        d.Id,
                        d.Name,
                        d.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id AND Id != new.Id), ''),
                        COALESCE(d.ExtractedText, '')
                    FROM JumpDocuments d WHERE d.Id = new.JumpDocumentId;
                    
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        d.Id,
                        d.Name,
                        d.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                        COALESCE(d.ExtractedText, '')
                    FROM JumpDocuments d WHERE d.Id = new.JumpDocumentId;
                END;
            ");

            // Trigger: Tag Update - update FTS5 when tags are modified
            migrationBuilder.Sql(@"
                CREATE TRIGGER DocumentTags_au AFTER UPDATE ON DocumentTags BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        'delete',
                        d.Id,
                        d.Name,
                        d.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                        COALESCE(d.ExtractedText, '')
                    FROM JumpDocuments d WHERE d.Id = old.JumpDocumentId;
                    
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        d.Id,
                        d.Name,
                        d.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                        COALESCE(d.ExtractedText, '')
                    FROM JumpDocuments d WHERE d.Id = new.JumpDocumentId;
                END;
            ");

            // Trigger: Tag Delete - update FTS5 when tags are removed
            migrationBuilder.Sql(@"
                CREATE TRIGGER DocumentTags_ad AFTER DELETE ON DocumentTags BEGIN
                    INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        'delete',
                        d.Id,
                        d.Name,
                        d.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id AND Id != old.Id), ''),
                        COALESCE(d.ExtractedText, '')
                    FROM JumpDocuments d WHERE d.Id = old.JumpDocumentId;
                    
                    INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                    SELECT 
                        d.Id,
                        d.Name,
                        d.FolderPath,
                        COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                        COALESCE(d.ExtractedText, '')
                    FROM JumpDocuments d WHERE d.Id = old.JumpDocumentId;
                END;
            ");

            // Populate FTS5 table with existing data
            migrationBuilder.Sql(@"
                INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    d.Id,
                    d.Name,
                    d.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                    COALESCE(d.ExtractedText, '')
                FROM JumpDocuments d;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop all triggers
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS DocumentTags_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS DocumentTags_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS DocumentTags_ai;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS JumpDocuments_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS JumpDocuments_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS JumpDocuments_ai;");
            
            // Drop FTS5 table
            migrationBuilder.Sql("DROP TABLE IF EXISTS JumpDocuments_fts;");
        }
    }
}
