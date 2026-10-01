using JumpChainSearch.Data;
using JumpChainSearch.Helpers;
using JumpChainSearch.Models;
using JumpChainSearch.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JumpChainSearch.Tests;

public sealed class DocumentDuplicateDetectionServiceTests
{
    [Fact]
    public async Task FindsCanonicalDriveId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await DatabaseFixture.CreateAsync(cancellationToken);
        var document = CreateDocument("canonical", "Canonical");
        fixture.Context.Add(document);
        await fixture.Context.SaveChangesAsync(cancellationToken);

        var match = await fixture.Detector.FindByDriveFileIdAsync("canonical", cancellationToken);

        Assert.NotNull(match);
        Assert.Equal(document.Id, match.DocumentId);
        Assert.Equal(DocumentDuplicateMatchKind.DriveFileId, match.Kind);
    }

    [Fact]
    public async Task FindsDriveIdStoredAsAlternateSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await DatabaseFixture.CreateAsync(cancellationToken);
        var document = CreateDocument("canonical", "Canonical");
        document.Urls.Add(CreateSource("alternate"));
        fixture.Context.Add(document);
        await fixture.Context.SaveChangesAsync(cancellationToken);

        var match = await fixture.Detector.FindByDriveFileIdAsync("alternate", cancellationToken);

        Assert.NotNull(match);
        Assert.Equal(document.Id, match.DocumentId);
        Assert.Equal(DocumentDuplicateMatchKind.DriveFileId, match.Kind);
    }

    [Fact]
    public async Task FindsBinaryChecksumStoredOnAlternateSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await DatabaseFixture.CreateAsync(cancellationToken);
        var document = CreateDocument("canonical", "Canonical");
        document.Urls.Add(CreateSource("source", "sha256:abc123"));
        fixture.Context.Add(document);
        await fixture.Context.SaveChangesAsync(cancellationToken);

        var candidate = CreateDocument("copy", "Renamed copy");
        candidate.BinaryContentHash = "sha256:abc123";
        var match = await fixture.Detector.FindByContentAsync(candidate, cancellationToken);

        Assert.NotNull(match);
        Assert.Equal(document.Id, match.DocumentId);
        Assert.Equal(DocumentDuplicateMatchKind.BinaryContent, match.Kind);
    }

    [Fact]
    public async Task FindsBinaryChecksumStoredOnDocument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await DatabaseFixture.CreateAsync(cancellationToken);
        var document = CreateDocument("canonical", "Canonical");
        document.BinaryContentHash = "sha256:def456";
        fixture.Context.Add(document);
        await fixture.Context.SaveChangesAsync(cancellationToken);

        var candidate = CreateDocument("copy", "Renamed copy");
        candidate.BinaryContentHash = "sha256:def456";
        var match = await fixture.Detector.FindByContentAsync(candidate, cancellationToken);

        Assert.NotNull(match);
        Assert.Equal(document.Id, match.DocumentId);
        Assert.Equal(DocumentDuplicateMatchKind.BinaryContent, match.Kind);
    }

    [Fact]
    public async Task FindsNormalizedExtractedTextWithDifferentDriveId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await DatabaseFixture.CreateAsync(cancellationToken);
        var text = string.Join('\n', Enumerable.Repeat("Identical document content.", 20));
        var document = CreateDocument("original", "Original");
        document.ExtractedText = text.Replace("\n", "\r\n", StringComparison.Ordinal);
        fixture.Context.Add(document);
        await fixture.Context.SaveChangesAsync(cancellationToken);

        var candidate = CreateDocument("copy", "Copy");
        candidate.ExtractedText = text;
        var match = await fixture.Detector.FindByContentAsync(candidate, cancellationToken);

        Assert.NotNull(match);
        Assert.Equal(document.Id, match.DocumentId);
        Assert.Equal(DocumentDuplicateMatchKind.ExtractedText, match.Kind);
    }

    [Fact]
    public async Task ComputesLegacyTextHashWhenStoredHashIsMissing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await DatabaseFixture.CreateAsync(cancellationToken);
        var text = string.Join(' ', Enumerable.Repeat("Legacy extracted content", 30));
        var document = CreateDocument("legacy", "Legacy");
        document.ExtractedText = text;
        fixture.Context.Add(document);
        await fixture.Context.SaveChangesAsync(cancellationToken);
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE JumpDocuments SET TextContentHash = NULL WHERE Id = {document.Id}",
            cancellationToken);
        fixture.Context.ChangeTracker.Clear();

        var candidate = CreateDocument("copy", "Renamed legacy copy");
        candidate.ExtractedText = text;
        var match = await fixture.Detector.FindByContentAsync(candidate, cancellationToken);

        Assert.NotNull(match);
        Assert.Equal(document.Id, match.DocumentId);
    }

    [Fact]
    public async Task DoesNotFingerprintShortExtractedText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await DatabaseFixture.CreateAsync(cancellationToken);
        var document = CreateDocument("short", "Short");
        document.ExtractedText = "A short shared header";
        fixture.Context.Add(document);
        await fixture.Context.SaveChangesAsync(cancellationToken);

        var candidate = CreateDocument("copy", "Copy");
        candidate.ExtractedText = document.ExtractedText;
        var match = await fixture.Detector.FindByContentAsync(candidate, cancellationToken);

        Assert.Null(DocumentFingerprint.FromExtractedText(candidate.ExtractedText));
        Assert.Null(match);
    }

    [Fact]
    public async Task DoesNotMatchDifferentExtractedText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await DatabaseFixture.CreateAsync(cancellationToken);
        var document = CreateDocument("original", "Same title");
        document.ExtractedText = string.Join(' ', Enumerable.Repeat("Original content", 30));
        fixture.Context.Add(document);
        await fixture.Context.SaveChangesAsync(cancellationToken);

        var candidate = CreateDocument("copy", "Same title");
        candidate.ExtractedText = string.Join(' ', Enumerable.Repeat("Entirely different content", 30));
        var match = await fixture.Detector.FindByContentAsync(candidate, cancellationToken);

        Assert.Null(match);
    }

    private static JumpDocument CreateDocument(string fileId, string name)
    {
        return new JumpDocument
        {
            GoogleDriveFileId = fileId,
            Name = name,
            MimeType = "application/pdf",
            Size = 1234,
            CreatedTime = DateTime.UtcNow,
            ModifiedTime = DateTime.UtcNow,
            LastScanned = DateTime.UtcNow,
            LastModified = DateTime.UtcNow,
            SourceDrive = "Test"
        };
    }

    private static DocumentUrl CreateSource(string fileId, string? binaryHash = null)
    {
        return new DocumentUrl
        {
            GoogleDriveFileId = fileId,
            SourceDrive = "Test",
            LastScanned = DateTime.UtcNow,
            BinaryContentHash = binaryHash
        };
    }

    private sealed class DatabaseFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private DatabaseFixture(SqliteConnection connection, JumpChainDbContext context)
        {
            _connection = connection;
            Context = context;
            Detector = new DocumentDuplicateDetectionService(context);
        }

        public JumpChainDbContext Context { get; }
        public DocumentDuplicateDetectionService Detector { get; }

        public static async Task<DatabaseFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<JumpChainDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new JumpChainDbContext(options);
            await context.Database.EnsureCreatedAsync(cancellationToken);
            return new DatabaseFixture(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}