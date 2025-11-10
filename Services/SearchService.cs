using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using JumpChainSearch.Models;

namespace JumpChainSearch.Services
{
    public interface ISearchService
    {
        Task<IEnumerable<JumpDocument>> SearchDocumentsAsync(string? query, string? driveFilter = null, string? tagFilter = null, int page = 1, int pageSize = 20);
        Task<IEnumerable<string>> GetAvailableTagsAsync(string? category = null);
        Task<IEnumerable<string>> GetAvailableDrivesAsync();
        Task<int> GetTotalDocumentCountAsync();
    }

    public class SearchService : ISearchService
    {
        private readonly JumpChainDbContext _context;
        private readonly ILogger<SearchService> _logger;

        public SearchService(JumpChainDbContext context, ILogger<SearchService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<JumpDocument>> SearchDocumentsAsync(
            string? query, 
            string? driveFilter = null, 
            string? tagFilter = null, 
            int page = 1, 
            int pageSize = 20)
        {
            try
            {
                var documentsQuery = _context.JumpDocuments
                    .Include(d => d.Tags)
                    .AsQueryable();

                // Apply drive filter
                if (!string.IsNullOrWhiteSpace(driveFilter))
                {
                    documentsQuery = documentsQuery.Where(d => d.SourceDrive == driveFilter);
                }

                // Apply tag filter
                if (!string.IsNullOrWhiteSpace(tagFilter))
                {
                    documentsQuery = documentsQuery.Where(d => 
                        d.Tags.Any(t => t.TagName == tagFilter));
                }

                // Apply text search with relevance scoring
                if (!string.IsNullOrWhiteSpace(query))
                {
                    var searchTerms = ParseSearchQuery(query);
                    
                    // Build OR filter - document must match at least one phrase
                    var matchFilter = documentsQuery.Where(d => false); // Start with empty
                    
                    foreach (var term in searchTerms)
                    {
                        var termLower = term.ToLower();
                        matchFilter = matchFilter.Union(
                            documentsQuery.Where(d => 
                                EF.Functions.Like(d.Name.ToLower(), $"%{termLower}%") ||
                                EF.Functions.Like(d.Description != null ? d.Description.ToLower() : "", $"%{termLower}%") ||
                                EF.Functions.Like(d.ExtractedText != null ? d.ExtractedText.ToLower() : "", $"%{termLower}%") ||
                                d.Tags.Any(t => EF.Functions.Like(t.TagName.ToLower(), $"%{termLower}%")))
                        );
                    }
                    
                    documentsQuery = matchFilter;
                    
                    // Fetch results and sort by relevance in memory
                    var allResults = await documentsQuery.ToListAsync();
                    
                    var scoredResults = allResults
                        .Select(doc => new 
                        { 
                            Document = doc, 
                            Score = CalculateRelevanceScore(doc, searchTerms) 
                        })
                        .OrderByDescending(x => x.Score)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(x => x.Document)
                        .ToList();
                    
                    _logger.LogInformation($"Search returned {scoredResults.Count} results for query: '{query}'");
                    return scoredResults;
                }
                else
                {
                    // No search query - just apply filters and order by modified time
                    var results = await documentsQuery
                        .OrderByDescending(d => d.ModifiedTime)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToListAsync();
                    
                    _logger.LogInformation($"Retrieved {results.Count} documents without search query");
                    return results;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching documents with query: '{query}'");
                return Enumerable.Empty<JumpDocument>();
            }
        }

        /// <summary>
        /// Parses a search query into individual terms and phrases.
        /// Handles quoted phrases and individual words.
        /// </summary>
        private List<string> ParseSearchQuery(string query)
        {
            var terms = new List<string>();
            var currentTerm = new System.Text.StringBuilder();
            bool inQuotes = false;
            
            for (int i = 0; i < query.Length; i++)
            {
                char c = query[i];
                
                if (c == '"')
                {
                    if (inQuotes)
                    {
                        // End of quoted phrase
                        if (currentTerm.Length > 0)
                        {
                            terms.Add(currentTerm.ToString());
                            currentTerm.Clear();
                        }
                        inQuotes = false;
                    }
                    else
                    {
                        // Start of quoted phrase - save any accumulated term first
                        if (currentTerm.Length > 0)
                        {
                            terms.Add(currentTerm.ToString());
                            currentTerm.Clear();
                        }
                        inQuotes = true;
                    }
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    // Space outside quotes - end current term
                    if (currentTerm.Length > 0)
                    {
                        terms.Add(currentTerm.ToString());
                        currentTerm.Clear();
                    }
                }
                else
                {
                    // Regular character - add to current term
                    currentTerm.Append(c);
                }
            }
            
            // Add final term if any
            if (currentTerm.Length > 0)
            {
                terms.Add(currentTerm.ToString());
            }
            
            return terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }

        /// <summary>
        /// Calculates relevance score for a document based on search terms.
        /// Higher scores indicate better matches.
        /// </summary>
        private double CalculateRelevanceScore(JumpDocument doc, List<string> searchTerms)
        {
            double score = 0;
            
            string docName = doc.Name?.ToLower() ?? "";
            string docDescription = doc.Description?.ToLower() ?? "";
            string docExtractedText = doc.ExtractedText?.ToLower() ?? "";
            var docTags = doc.Tags?.Select(t => t.TagName.ToLower()).ToList() ?? new List<string>();
            
            foreach (var term in searchTerms)
            {
                string termLower = term.ToLower();
                
                // Title matches are worth the most (1000 points per match)
                score += CountOccurrences(docName, termLower) * 1000;
                
                // Description matches are worth medium points (100 points per match)
                score += CountOccurrences(docDescription, termLower) * 100;
                
                // Tag matches are worth good points (500 points per match)
                foreach (var tag in docTags)
                {
                    score += CountOccurrences(tag, termLower) * 500;
                }
                
                // Content matches are worth least (1 point per match)
                score += CountOccurrences(docExtractedText, termLower) * 1;
                
                // Bonus for exact title match
                if (docName == termLower)
                {
                    score += 10000;
                }
                
                // Bonus for title starts with term
                if (docName.StartsWith(termLower))
                {
                    score += 5000;
                }
            }
            
            return score;
        }

        /// <summary>
        /// Counts case-insensitive occurrences of a search term in text.
        /// </summary>
        private int CountOccurrences(string text, string term)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
                return 0;
            
            int count = 0;
            int index = 0;
            
            while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                count++;
                index += term.Length;
            }
            
            return count;
        }

        public async Task<IEnumerable<string>> GetAvailableTagsAsync(string? category = null)
        {
            try
            {
                var tagsQuery = _context.DocumentTags.AsQueryable();

                if (!string.IsNullOrWhiteSpace(category))
                {
                    tagsQuery = tagsQuery.Where(t => t.TagCategory == category);
                }

                var tags = await tagsQuery
                    .Select(t => t.TagName)
                    .Distinct()
                    .OrderBy(t => t)
                    .ToListAsync();

                return tags;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting available tags for category: '{category}'");
                return Enumerable.Empty<string>();
            }
        }

        public async Task<IEnumerable<string>> GetAvailableDrivesAsync()
        {
            try
            {
                var drives = await _context.JumpDocuments
                    .Select(d => d.SourceDrive)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToListAsync();

                return drives;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available drives");
                return Enumerable.Empty<string>();
            }
        }

        public async Task<int> GetTotalDocumentCountAsync()
        {
            try
            {
                return await _context.JumpDocuments.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total document count");
                return 0;
            }
        }
    }

    public enum DocumentFormatType
    {
        Unknown,
        JumpChainStandard,  // "Name (+100 CP)" format
        ColonDelimited,     // "Item Name: Description" format
        Mixed               // Multiple formats in same document
    }

    public class DocumentFormatAnalysis
    {
        public DocumentFormatType FormatType { get; set; } = DocumentFormatType.Unknown;
        public double Confidence { get; set; } = 0.0;
        public int JumpChainPatternCount { get; set; } = 0;
        public int ColonPatternCount { get; set; } = 0;
        public int TotalAnalyzedLines { get; set; } = 0;
        public List<string> SampleJumpChainLines { get; set; } = new();
        public List<string> SampleColonLines { get; set; } = new();
    }

    public interface IPurchasableParsingService
    {
        Task<List<DocumentPurchasable>> ParseDocumentAsync(JumpDocument document);
        Task<int> ParseAndSaveDocumentAsync(JumpDocument document);
        Task<int> ParseMultipleDocumentsAsync(IEnumerable<int> documentIds);
    }

    public class PurchasableParsingService : IPurchasableParsingService
    {
        private readonly JumpChainDbContext _context;
        private readonly ILogger<PurchasableParsingService> _logger;

        public PurchasableParsingService(JumpChainDbContext context, ILogger<PurchasableParsingService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public Task<List<DocumentPurchasable>> ParseDocumentAsync(JumpDocument document)
        {
            if (string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                return Task.FromResult(new List<DocumentPurchasable>());
            }

            // Step 1: Analyze document format to determine parsing strategy
            var formatAnalysis = AnalyzeDocumentFormat(document.ExtractedText);
            _logger.LogInformation($"Document {document.Name} format analysis: {formatAnalysis.FormatType} (confidence: {formatAnalysis.Confidence:F2})");

            // Step 2: Apply format-specific parsing
            var purchasables = formatAnalysis.FormatType switch
            {
                DocumentFormatType.JumpChainStandard => ParseJumpChainStandardFormat(document, formatAnalysis),
                DocumentFormatType.ColonDelimited => ParseColonDelimitedFormat(document, formatAnalysis),
                DocumentFormatType.Mixed => ParseMixedFormat(document, formatAnalysis),
                _ => ParseFallbackFormat(document, formatAnalysis)
            };

            _logger.LogInformation($"Parsed {purchasables.Count} purchasables from document {document.Name} using {formatAnalysis.FormatType} parser");
            return Task.FromResult(purchasables);
        }

        private DocumentFormatAnalysis AnalyzeDocumentFormat(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var analysis = new DocumentFormatAnalysis();
            
            // Count different format patterns
            int jumpChainCPCount = 0;
            int colonDelimitedCount = 0;
            int totalLines = 0;

            foreach (var line in lines.Take(100)) // Analyze first 100 lines for performance
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 10) continue;
                totalLines++;

                // Pattern 1: JumpChain standard "Name (+100 CP)"
                if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^.+\s*\([+\-]?\d+\s*CP\)", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    jumpChainCPCount++;
                    analysis.SampleJumpChainLines.Add(trimmed);
                }

                // Pattern 2: Colon-delimited "Item Name: Description"
                var colonMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"^([A-Za-z][A-Za-z0-9\s\-']{2,50}):\s*(.{10,})");
                if (colonMatch.Success && IsValidItemName(colonMatch.Groups[1].Value.Trim()))
                {
                    colonDelimitedCount++;
                    analysis.SampleColonLines.Add(trimmed);
                }
            }

            // Determine primary format based on pattern prevalence
            var jumpChainRatio = totalLines > 0 ? (double)jumpChainCPCount / totalLines : 0;
            var colonRatio = totalLines > 0 ? (double)colonDelimitedCount / totalLines : 0;

            if (jumpChainCPCount >= 3 && jumpChainRatio > colonRatio)
            {
                analysis.FormatType = DocumentFormatType.JumpChainStandard;
                analysis.Confidence = Math.Min(0.95, 0.5 + jumpChainRatio);
            }
            else if (colonDelimitedCount >= 3 && colonRatio > jumpChainRatio)
            {
                analysis.FormatType = DocumentFormatType.ColonDelimited;
                analysis.Confidence = Math.Min(0.95, 0.5 + colonRatio);
            }
            else if (jumpChainCPCount >= 1 && colonDelimitedCount >= 1)
            {
                analysis.FormatType = DocumentFormatType.Mixed;
                analysis.Confidence = 0.7;
            }
            else
            {
                analysis.FormatType = DocumentFormatType.Unknown;
                analysis.Confidence = 0.3;
            }

            analysis.JumpChainPatternCount = jumpChainCPCount;
            analysis.ColonPatternCount = colonDelimitedCount;
            analysis.TotalAnalyzedLines = totalLines;

            return analysis;
        }

        private List<DocumentPurchasable> ParseJumpChainStandardFormat(JumpDocument document, DocumentFormatAnalysis analysis)
        {
            var purchasables = new List<DocumentPurchasable>();
            if (string.IsNullOrEmpty(document.ExtractedText)) return purchasables;
            
            var lines = document.ExtractedText.Split('\n', StringSplitOptions.None);
            var currentCategory = "Items";

            _logger.LogInformation($"Using JumpChain Standard parser for {document.Name}");

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Update category if detected
                var category = DetectSimpleCategory(line);
                if (!string.IsNullOrEmpty(category))
                {
                    currentCategory = category;
                    continue;
                }

                // Parse JumpChain format: "Name (+100 CP)"
                var pattern = @"^(.+?)\s*\(([+\-]?\d+)\s*CP\)";
                var match = System.Text.RegularExpressions.Regex.Match(line, pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    var name = match.Groups[1].Value.Trim();
                    var costValue = int.Parse(match.Groups[2].Value.Replace("+", "").Replace("-", ""));

                    if (!string.IsNullOrWhiteSpace(name) && name.Length >= 2 && name.Length <= 100)
                    {
                        var description = ExtractNextLineDescription(lines, i);
                        var costs = new List<PurchasableCost> { new PurchasableCost { Value = costValue, Currency = "CP" } };

                        purchasables.Add(new DocumentPurchasable
                        {
                            JumpDocumentId = document.Id,
                            Name = name,
                            Category = currentCategory,
                            Description = description,
                            CostsJson = System.Text.Json.JsonSerializer.Serialize(costs),
                            PrimaryCost = costValue,
                            LineNumber = i + 1,
                            CharacterPosition = 0
                        });

                        _logger.LogInformation($"Parsed JumpChain item: '{name}' ({costValue} CP) in category '{currentCategory}'");
                    }
                }
            }

            return purchasables;
        }

        private List<DocumentPurchasable> ParseColonDelimitedFormat(JumpDocument document, DocumentFormatAnalysis analysis)
        {
            var purchasables = new List<DocumentPurchasable>();
            if (string.IsNullOrEmpty(document.ExtractedText)) return purchasables;
            
            var lines = document.ExtractedText.Split('\n', StringSplitOptions.None);
            var currentCategory = "Items";

            _logger.LogInformation($"Using Colon Delimited parser for {document.Name}");

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Update category if detected
                var category = DetectSimpleCategory(line);
                if (!string.IsNullOrEmpty(category))
                {
                    currentCategory = category;
                    continue;
                }

                // Parse colon format: "Item Name: Description"
                var pattern = @"^([A-Za-z][A-Za-z0-9\s\-']{2,50}):\s*(.+)";
                var match = System.Text.RegularExpressions.Regex.Match(line, pattern);

                if (match.Success)
                {
                    var name = match.Groups[1].Value.Trim();
                    var description = match.Groups[2].Value.Trim();

                    if (IsValidItemName(name))
                    {
                        var fullDescription = description + " " + ExtractNextLineDescription(lines, i);
                        var costs = new List<PurchasableCost> { new PurchasableCost { Value = 0, Currency = "CP" } };

                        purchasables.Add(new DocumentPurchasable
                        {
                            JumpDocumentId = document.Id,
                            Name = name,
                            Category = currentCategory,
                            Description = fullDescription.Trim(),
                            CostsJson = System.Text.Json.JsonSerializer.Serialize(costs),
                            PrimaryCost = 0,
                            LineNumber = i + 1,
                            CharacterPosition = 0
                        });

                        _logger.LogInformation($"Parsed colon item: '{name}' in category '{currentCategory}'");
                    }
                }
            }

            return purchasables;
        }

        private List<DocumentPurchasable> ParseMixedFormat(JumpDocument document, DocumentFormatAnalysis analysis)
        {
            // For mixed format, try both parsers and combine results
            var jumpChainResults = ParseJumpChainStandardFormat(document, analysis);
            var colonResults = ParseColonDelimitedFormat(document, analysis);

            var combined = new List<DocumentPurchasable>();
            combined.AddRange(jumpChainResults);
            combined.AddRange(colonResults);

            // Remove duplicates based on name and line number
            return combined.GroupBy(p => new { p.Name, p.LineNumber })
                          .Select(g => g.First())
                          .OrderBy(p => p.LineNumber)
                          .ToList();
        }

        private List<DocumentPurchasable> ParseFallbackFormat(JumpDocument document, DocumentFormatAnalysis analysis)
        {
            // Fallback to trying both formats with lower confidence
            _logger.LogInformation($"Using fallback parser for {document.Name}");
            return ParseMixedFormat(document, analysis);
        }

        private string DetectSimpleCategory(string line)
        {
            var lower = line.ToLowerInvariant().Trim();
            
            // Simple category detection for common patterns
            var categories = new Dictionary<string, string[]>
            {
                ["Drawbacks"] = new[] { "drawbacks", "disadvantages", "flaws" },
                ["Perks"] = new[] { "perks", "abilities", "powers" },
                ["Items"] = new[] { "items", "equipment", "gear" },
                ["Companions"] = new[] { "companions", "followers" },
                ["Scenarios"] = new[] { "scenarios", "challenges" },
                ["Resources"] = new[] { "resources", "wealth" }
            };

            // Only match short lines that look like headers
            if (line.Length > 50) return string.Empty;

            foreach (var category in categories)
            {
                if (category.Value.Any(pattern => lower.Equals(pattern) || lower.StartsWith(pattern + " ")))
                {
                    return category.Key;
                }
            }

            return string.Empty;
        }

        private string ExtractNextLineDescription(string[] lines, int currentIndex)
        {
            if (currentIndex + 1 < lines.Length)
            {
                var nextLine = lines[currentIndex + 1].Trim();
                // Only include if it doesn't look like another item or category
                if (!string.IsNullOrWhiteSpace(nextLine) && 
                    !System.Text.RegularExpressions.Regex.IsMatch(nextLine, @"^.+\s*\([+\-]?\d+\s*CP\)") &&
                    !nextLine.Contains(":") &&
                    nextLine.Length < 200)
                {
                    return nextLine;
                }
            }
            return string.Empty;
        }

        private bool IsLikelyHeader(string line)
        {
            // Heuristics for detecting section headers
            return line.Length < 50 || 
                   line.All(c => char.IsUpper(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c)) ||
                   line.StartsWith("=") || line.EndsWith("=") ||
                   line.StartsWith("-") && line.EndsWith("-");
        }



        private bool IsValidItemName(string name)
        {
            var lower = name.ToLowerInvariant();
            
            // Reject if it contains sentence-like patterns
            if (lower.Contains("will be") || lower.Contains("you'll") || lower.Contains("let's") ||
                lower.Contains("more") || lower.Contains("even") || lower.Contains("also") ||
                lower.Contains("this") || lower.Contains("that") || lower.Contains("so"))
            {
                return false;
            }
            
            // Reject if it starts with common sentence starters
            if (lower.StartsWith("in ") || lower.StartsWith("by ") || lower.StartsWith("with ") ||
                lower.StartsWith("from ") || lower.StartsWith("as ") || lower.StartsWith("through ") ||
                lower.StartsWith("during ") || lower.StartsWith("after ") || lower.StartsWith("before "))
            {
                return false;
            }
            
            // Accept if it looks like a proper item name (starts with capital, reasonable length)
            return name.Length >= 3 && name.Length <= 50 && 
                   char.IsUpper(name[0]) && 
                   !name.Contains("  "); // No double spaces
        }
        
        private bool IsNonPurchasableLine(string line)
        {
            var lowerLine = line.ToLowerInvariant();
            
            // Skip common non-purchasable patterns
            var skipPatterns = new[]
            {
                @"^(note|warning|important|remember):", // Instructions
                @"^(you|your|the)\s", // Descriptive text starting with articles
                @"^(this|that|these|those)\s", // Demonstrative pronouns
                @"^(all|some|many|most)\s", // Quantifiers
                @"^\d+\s+(years?|months?|days?)\s", // Time periods
                @"^(roll|choose|select|pick)\s", // Instructions
                @"^(if|when|while|during)\s", // Conditional text
                @"^(however|therefore|meanwhile|additionally)\s", // Connectors
            };
            
            return skipPatterns.Any(pattern => 
                System.Text.RegularExpressions.Regex.IsMatch(lowerLine, pattern));
        }
        
        private bool IsDescriptionText(string text)
        {
            var lowerText = text.ToLowerInvariant();
            
            // Check for common description indicators
            var descriptionIndicators = new[]
            {
                "you gain", "you get", "you can", "you may", "you will",
                "this gives", "this allows", "this provides", "this grants",
                "allows you to", "gives you", "grants you", "provides you",
                "body mod", "and anything else", "begin with", "other hand"
            };
            
            return descriptionIndicators.Any(indicator => lowerText.Contains(indicator));
        }

        private List<PurchasableCost> ExtractCosts(string text)
        {
            var costs = new List<PurchasableCost>();
            
            // Check for "Free" items first
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(free|0\s*cp)\b", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                costs.Add(new PurchasableCost { Value = 0, Currency = "CP" });
            }
            
            // Enhanced patterns for costs - JumpChain specific formats
            var costPatterns = new[]
            {
                // JumpChain drawback format: (+100 CP), (-50 CP)
                @"\(([+\-]?\d+(?:/\d+)*)\s*(CP|cp|points?|pts?)\)",
                
                // Standard parentheses: (100 CP), (200), etc.
                @"\((\d+(?:/\d+)*)\s*(?:(CP|cp|points?|pts?))?\)",
                
                // Square brackets: [200 CP], [100], etc.
                @"\[([+\-]?\d+(?:/\d+)*)\s*(?:(CP|cp|points?|pts?))?\]",
                
                // Tiered pricing: 50/100/200 CP, 100/200 CP, etc.
                @"(\d+(?:/\d+)*)\s*(CP|cp|points?|pts?)(?!\w)",
                
                // Cost at end of line: "Item Name 300 CP"
                @"(?:^|[^\d])(\d+)\s*(CP|cp|points?|pts?)(?:\s*$|\s*[^\w])",
                
                // Dash separated: "Item Name - 100 CP"
                @"[-–—]\s*([+\-]?\d+)\s*(CP|cp|points?|pts?)",
                
                // Colon separated: "Item Name: 100 CP"
                @":\s*([+\-]?\d+)\s*(CP|cp|points?|pts?)"
            };

            foreach (var pattern in costPatterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | 
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var costText = match.Groups[1].Value;
                    var currency = match.Groups.Count > 2 && !string.IsNullOrEmpty(match.Groups[2].Value) 
                        ? match.Groups[2].Value.ToUpperInvariant() 
                        : "CP";
                    
                    // Handle tiered pricing (e.g., "50/100/200")
                    if (costText.Contains('/'))
                    {
                        var tierValues = costText.Split('/');
                        foreach (var tier in tierValues)
                        {
                            var cleanTier = tier.Trim().TrimStart('+'); // Remove + sign for parsing
                            if (int.TryParse(cleanTier, out int value))
                            {
                                costs.Add(new PurchasableCost 
                                { 
                                    Value = Math.Abs(value), // Use absolute value, sign indicates drawback/perk
                                    Currency = currency 
                                });
                            }
                        }
                    }
                    else 
                    {
                        var cleanCost = costText.Trim().TrimStart('+'); // Remove + sign for parsing
                        if (int.TryParse(cleanCost, out int singleValue))
                        {
                            costs.Add(new PurchasableCost 
                            { 
                                Value = Math.Abs(singleValue), // Use absolute value
                                Currency = currency 
                            });
                        }
                    }
                }
            }

            // Remove duplicates and sort by value
            return costs.GroupBy(c => new { c.Value, c.Currency })
                       .Select(g => g.First())
                       .OrderBy(c => c.Value)
                       .ToList();
        }

        private (string name, string description) ExtractNameAndDescription(string line, string[] allLines, int currentIndex)
        {
            var name = ExtractPurchasableName(line);
            var description = ExtractPurchasableDescription(line, allLines, currentIndex);
            
            return (name, description);
        }
        
        private string ExtractPurchasableName(string line)
        {
            var cleanLine = line.Trim();
            
            // Remove common prefixes (bullet points, numbers, etc.)
            cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, 
                @"^[\-\•\*]+\s*", "").Trim();
            cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, 
                @"^\d+[\.\)]\s*", "").Trim();
            
            // JumpChain specific patterns - stop at cost indicators including (+100 CP) format
            var costStopPatterns = new[]
            {
                @"\s*\([+\-]?\d+(?:/\d+)*\s*(?:CP|cp|points?|pts?)\)", // "(+100 CP)", "(-50 CP)", "(100 CP)"
                @"\s*\[[+\-]?\d+(?:/\d+)*\s*(?:CP|cp|points?|pts?)\]", // "[+100 CP]"
                @"\s*\([+\-]?\d+(?:/\d+)*\)", // "(+100)", "(-50)", "(100)"
                @"\s*\[[+\-]?\d+(?:/\d+)*\]", // "[+100]", "[-50]", "[100]"
                @"\s*[-–—]\s*[+\-]?\d+", // "Name - +100", "Name - 100"
                @"\s*:\s*[+\-]?\d+", // "Name: +100"
                @"\s+[+\-]?\d+\s*(CP|cp|points?|pts?)", // "Name +100 CP"
            };
            
            var name = cleanLine;
            foreach (var pattern in costStopPatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(cleanLine, pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    name = cleanLine.Substring(0, match.Index).Trim();
                    break;
                }
            }
            
            // Clean up the name further
            name = name.Trim(' ', '-', ':', '•', '*');
            
            // Remove trailing punctuation that's not part of the name
            name = System.Text.RegularExpressions.Regex.Replace(name, @"[,:;]\s*$", "").Trim();
            
            return name;
        }
        
        private string ExtractPurchasableDescription(string nameOnlyLine, string[] allLines, int currentIndex)
        {
            var description = new List<string>();
            
            // Check if there's description text after the cost on the same line
            var mainLine = allLines[currentIndex].Trim();
            
            // Find where the cost ends to get remaining description
            var costPatterns = new[]
            {
                @"\([+\-]?\d+(?:/\d+)*\s*(?:CP|cp|points?|pts?)\)", // "(+100 CP)"
                @"\[[+\-]?\d+(?:/\d+)*\s*(?:CP|cp|points?|pts?)\]", // "[+100 CP]"
            };
            
            var remainingText = "";
            foreach (var pattern in costPatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(mainLine, pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var afterCost = mainLine.Substring(match.Index + match.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(afterCost) && afterCost.Length > 5)
                    {
                        remainingText = afterCost;
                        break;
                    }
                }
            }
            
            if (!string.IsNullOrWhiteSpace(remainingText))
            {
                description.Add(remainingText);
            }
            
            // Look for description in subsequent lines - JumpChain descriptions are often multi-line
            for (int i = currentIndex + 1; i < Math.Min(currentIndex + 10, allLines.Length); i++)
            {
                var nextLine = allLines[i].Trim();
                if (string.IsNullOrWhiteSpace(nextLine)) continue;
                
                // Stop if we hit another purchasable (line with cost pattern)
                if (HasCostPattern(nextLine)) break;
                
                // Stop if we hit a category header
                if (IsLikelyHeader(nextLine)) break;
                
                // Stop if line looks like a new purchasable name (typical JumpChain formatting)
                if (System.Text.RegularExpressions.Regex.IsMatch(nextLine, @"^[A-Z][a-zA-Z\s]+ \([+\-]?\d+"))
                {
                    break;
                }
                
                // Add this line to description
                description.Add(nextLine);
                
                // Stop if we have a substantial description (JumpChain descriptions are usually 1-3 sentences)
                if (description.Sum(d => d.Length) > 300) break;
            }

            return string.Join(" ", description).Trim();
        }
        
        private bool HasCostPattern(string line)
        {
            var costPatterns = new[]
            {
                @"\([+\-]?\d+(?:/\d+)*\s*(?:CP|cp|points?|pts?)\)", // "(+100 CP)"
                @"\[[+\-]?\d+(?:/\d+)*\s*(?:CP|cp|points?|pts?)\]", // "[+100 CP]"
                @"\([+\-]?\d+(?:/\d+)*\)", // "(+100)"
                @"\[[+\-]?\d+(?:/\d+)*\]", // "[+100]"
            };
            
            return costPatterns.Any(pattern => 
                System.Text.RegularExpressions.Regex.IsMatch(line, pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        public async Task<int> ParseAndSaveDocumentAsync(JumpDocument document)
        {
            try
            {
                // Remove existing purchasables for this document
                var existingPurchasables = await _context.DocumentPurchasables
                    .Where(p => p.JumpDocumentId == document.Id)
                    .ToListAsync();
                
                _context.DocumentPurchasables.RemoveRange(existingPurchasables);

                // Parse new purchasables
                var newPurchasables = await ParseDocumentAsync(document);
                
                if (newPurchasables.Any())
                {
                    await _context.DocumentPurchasables.AddRangeAsync(newPurchasables);
                    await _context.SaveChangesAsync();
                }

                _logger.LogInformation($"Saved {newPurchasables.Count} purchasables for document {document.Name}");
                return newPurchasables.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error parsing purchasables for document {document.Name}");
                return 0;
            }
        }

        public async Task<int> ParseMultipleDocumentsAsync(IEnumerable<int> documentIds)
        {
            var totalParsed = 0;
            
            foreach (var documentId in documentIds)
            {
                var document = await _context.JumpDocuments
                    .Where(d => d.Id == documentId && !string.IsNullOrEmpty(d.ExtractedText))
                    .FirstOrDefaultAsync();
                
                if (document != null)
                {
                    var count = await ParseAndSaveDocumentAsync(document);
                    totalParsed += count;
                }
            }

            return totalParsed;
        }
    }
}