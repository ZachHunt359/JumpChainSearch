using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using JumpChainSearch.Models;
using JumpChainSearch.Helpers;

var optionsBuilder = new DbContextOptionsBuilder<JumpChainDbContext>();
optionsBuilder.UseSqlite("Data Source=C:\\Users\\khayy\\source\\repos\\JumpChainSearch\\jumpchain.db");

using var context = new JumpChainDbContext(optionsBuilder.Options);

Console.WriteLine("Removing existing Series/Franchise tags...");
var existingTags = await context.DocumentTags
    .Where(t => t.TagCategory == "Series" || t.TagCategory == "Franchise")
    .ToListAsync();
context.DocumentTags.RemoveRange(existingTags);
await context.SaveChangesAsync();
Console.WriteLine($"Removed {existingTags.Count} existing tags");

Console.WriteLine("Loading all documents...");
var allDocuments = await context.JumpDocuments.ToListAsync();
Console.WriteLine($"Found {allDocuments.Count} documents");

Console.WriteLine("Generating new series tags...");
var newTags = new List<DocumentTag>();
foreach (var document in allDocuments)
{
    var docTags = new List<DocumentTag>();
    TagGenerationHelpers.AddSeriesTags(docTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
    newTags.AddRange(docTags);
}

Console.WriteLine($"Generated {newTags.Count} new tags for {newTags.Select(t => t.JumpDocumentId).Distinct().Count()} documents");

context.DocumentTags.AddRange(newTags);
await context.SaveChangesAsync();

Console.WriteLine("Done! Verifying results...");
var verification = await context.DocumentTags
    .Where(t => t.TagCategory == "Series")
    .GroupBy(t => t.TagName)
    .Select(g => new { Series = g.Key, Count = g.Count() })
    .OrderByDescending(x => x.Count)
    .ToListAsync();

foreach (var item in verification)
{
    Console.WriteLine($"  {item.Series}: {item.Count}");
}
