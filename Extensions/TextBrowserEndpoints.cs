using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using JumpChainSearch.Services;

namespace JumpChainSearch.Extensions;

/// <summary>
/// HTML browser interfaces for viewing and searching extracted text content.
/// These endpoints provide interactive UIs for browsing document text.
/// </summary>
public static class TextBrowserEndpoints
{
    public static RouteGroupBuilder MapTextBrowserEndpoints(this RouteGroupBuilder group)
    {
        #pragma warning disable ASP0016 // Do not return Task<IResult> from routes - this is intentional for async HTML generation
        group.MapGet("/browse", BrowseText);
        group.MapGet("/browse/{documentId:int}", GetDocumentText);
        group.MapGet("/ui", TextBrowserUI);
        group.MapGet("/parser-test", ParserTestUI);
        group.MapPost("/flag-review/{documentId:int}", FlagForReview);
        group.MapPost("/unflag-review/{documentId:int}", UnflagReview);
        group.MapPost("/save-text/{documentId:int}", SaveEditedText);
        group.MapGet("/check-admin", CheckAdminStatus);
        #pragma warning restore ASP0016
        return group;
    }

    private static async Task<IResult> BrowseText(
        JumpChainDbContext context,
        int page = 1,
        int limit = 10,
        string? search = "",
        string? sortBy = "id",
        string? sortOrder = "asc",
        bool? hasText = null,
        string? extractionMethod = "")
    {
        try
        {
            var query = context.JumpDocuments.AsQueryable();
            
            // Filter by text presence
            if (hasText.HasValue)
            {
                if (hasText.Value)
                    query = query.Where(d => d.ExtractedText != null && d.ExtractedText.Length > 0);
                else
                    query = query.Where(d => d.ExtractedText == null || d.ExtractedText == "");
            }
            
            // Filter by extraction method
            if (!string.IsNullOrWhiteSpace(extractionMethod))
            {
                query = query.Where(d => d.ExtractionMethod == extractionMethod);
            }
            
            // Search in document name or extracted text
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(d => 
                    (d.Name != null && d.Name.Contains(search)) ||
                    (d.ExtractedText != null && d.ExtractedText.Contains(search)));
            }
            
            // Apply sorting with null safety
            var sortByLower = sortBy?.ToLower() ?? "id";
            var sortOrderLower = sortOrder?.ToLower() ?? "asc";
            
            query = sortByLower switch
            {
                "name" => sortOrderLower == "desc" ? query.OrderByDescending(d => d.Name) : query.OrderBy(d => d.Name),
                "size" => sortOrderLower == "desc" ? query.OrderByDescending(d => d.Size) : query.OrderBy(d => d.Size),
                "textlength" => sortOrderLower == "desc" ? 
                    query.OrderByDescending(d => d.ExtractedText != null ? d.ExtractedText.Length : 0) : 
                    query.OrderBy(d => d.ExtractedText != null ? d.ExtractedText.Length : 0),
                "method" => sortOrderLower == "desc" ? query.OrderByDescending(d => d.ExtractionMethod) : query.OrderBy(d => d.ExtractionMethod),
                _ => sortOrderLower == "desc" ? query.OrderByDescending(d => d.Id) : query.OrderBy(d => d.Id)
            };
            
            // Get total count for pagination
            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / limit);
            
            // Apply pagination
            var documents = await query
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(d => new {
                    d.Id,
                    d.Name,
                    d.MimeType,
                    d.Size,
                    d.FolderPath,
                    d.SourceDrive,
                    d.ExtractionMethod,
                    HasExtractedText = !string.IsNullOrEmpty(d.ExtractedText),
                    ExtractedTextLength = d.ExtractedText != null ? d.ExtractedText.Length : 0,
                    ExtractedTextPreview = d.ExtractedText != null && d.ExtractedText.Length > 200 
                        ? d.ExtractedText.Substring(0, 200) + "..." 
                        : d.ExtractedText ?? "",
                    WordCount = d.ExtractedText != null ? d.ExtractedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length : 0
                })
                .ToListAsync();
            
            return Results.Ok(new {
                success = true,
                page,
                limit,
                totalCount,
                totalPages,
                hasNext = page < totalPages,
                hasPrevious = page > 1,
                search,
                sortBy,
                sortOrder,
                hasText,
                extractionMethod,
                documents
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message 
            });
        }
    }

    private static async Task<IResult> GetDocumentText(JumpChainDbContext context, int documentId)
    {
        try
        {
            var rawDocument = await context.JumpDocuments
                .Where(d => d.Id == documentId)
                .FirstOrDefaultAsync();
                
            if (rawDocument == null)
            {
                return Results.NotFound(new { success = false, message = "Document not found" });
            }
            
            var document = new {
                rawDocument.Id,
                rawDocument.Name,
                rawDocument.MimeType,
                rawDocument.Size,
                rawDocument.FolderPath,
                rawDocument.SourceDrive,
                rawDocument.ExtractionMethod,
                rawDocument.ExtractedText,
                rawDocument.GoogleDriveFileId,
                rawDocument.TextNeedsReview,
                rawDocument.TextReviewFlaggedAt,
                rawDocument.TextReviewFlaggedBy,
                rawDocument.TextLastEditedAt,
                rawDocument.TextLastEditedBy,
                HasExtractedText = !string.IsNullOrEmpty(rawDocument.ExtractedText),
                ExtractedTextLength = rawDocument.ExtractedText?.Length ?? 0,
                WordCount = rawDocument.ExtractedText?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0,
                LineCount = rawDocument.ExtractedText?.Split('\n').Length ?? 0
            };
            
            return Results.Ok(new {
                success = true,
                document
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message 
            });
        }
    }

    private static async Task<IResult> FlagForReview(
        JumpChainDbContext context, 
        int documentId, 
        HttpContext httpContext)
    {
        try
        {
            var document = await context.JumpDocuments.FindAsync(documentId);
            if (document == null)
                return Results.NotFound(new { success = false, message = "Document not found" });

            // Get user identifier (IP address if not logged in, username if admin)
            var userIdentifier = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var sessionToken = httpContext.Request.Cookies["admin_session"];
            if (!string.IsNullOrEmpty(sessionToken))
            {
                // Try to get admin username
                var adminSession = await context.AdminSessions
                    .Include(s => s.AdminUser)
                    .FirstOrDefaultAsync(s => s.SessionToken == sessionToken && s.ExpiresAt > DateTime.UtcNow);
                if (adminSession != null)
                    userIdentifier = adminSession.AdminUser.Username;
            }

            document.TextNeedsReview = true;
            document.TextReviewFlaggedAt = DateTime.UtcNow;
            document.TextReviewFlaggedBy = userIdentifier;

            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Document flagged for review",
                flaggedBy = userIdentifier
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message 
            });
        }
    }

    private static async Task<IResult> UnflagReview(
        JumpChainDbContext context, 
        int documentId)
    {
        try
        {
            var document = await context.JumpDocuments.FindAsync(documentId);
            if (document == null)
                return Results.NotFound(new { success = false, message = "Document not found" });

            document.TextNeedsReview = false;

            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Review flag removed"
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message 
            });
        }
    }

    private static async Task<IResult> SaveEditedText(
        HttpContext httpContext,
        JumpChainDbContext context,
        AdminAuthService authService,
        int documentId,
        [FromBody] EditTextRequest request)
    {
        try
        {
            // Validate admin session
            var sessionToken = httpContext.Request.Cookies["admin_session"];
            if (string.IsNullOrEmpty(sessionToken))
                return Results.Unauthorized();

            var (valid, user) = await authService.ValidateSessionAsync(sessionToken);
            if (!valid || user == null)
                return Results.Unauthorized();

            var document = await context.JumpDocuments.FindAsync(documentId);
            if (document == null)
                return Results.NotFound(new { success = false, message = "Document not found" });

            document.ExtractedText = request.Text;
            document.TextLastEditedAt = DateTime.UtcNow;
            document.TextLastEditedBy = user.Username;
            document.TextNeedsReview = false; // Clear review flag when edited

            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Text saved successfully",
                editedBy = user.Username,
                editedAt = document.TextLastEditedAt
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message 
            });
        }
    }

    private static async Task<IResult> CheckAdminStatus(
        HttpContext httpContext,
        AdminAuthService authService)
    {
        try
        {
            var sessionToken = httpContext.Request.Cookies["admin_session"];
            if (string.IsNullOrEmpty(sessionToken))
                return Results.Ok(new { isAdmin = false });

            var (valid, user) = await authService.ValidateSessionAsync(sessionToken);
            
            return Results.Ok(new { 
                isAdmin = valid && user != null,
                username = user?.Username
            });
        }
        catch
        {
            return Results.Ok(new { isAdmin = false });
        }
    }

    public class EditTextRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    private static async Task<IResult> TextBrowserUI(HttpContext context)
    {
        // Large HTML content embedded directly
        var html = GetTextBrowserHtml();
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(html);
        return Results.Empty;
    }

    private static async Task<IResult> ParserTestUI(HttpContext context)
    {
        // Large HTML content embedded directly  
        var html = GetParserTestHtml();
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(html);
        return Results.Empty;
    }

    // Helper method to keep HTML separate
    private static string GetTextBrowserHtml()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>JumpChain Text Browser</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 0; padding: 20px; background: #f5f5f5; }
        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { border-bottom: 2px solid #007acc; padding-bottom: 15px; margin-bottom: 20px; }
        .header h1 { margin: 0; color: #007acc; }
        .controls { margin-bottom: 20px; padding: 15px; background: #f8f9fa; border-radius: 5px; }
        .controls input, .controls select, .controls button { margin: 5px; padding: 8px; border: 1px solid #ddd; border-radius: 4px; }
        .controls button { background: #007acc; color: white; cursor: pointer; border: none; }
        .controls button:hover { background: #005999; }
        .stats { background: #e8f4f8; padding: 10px; border-radius: 5px; margin-bottom: 15px; font-weight: bold; }
        .document-card { border: 1px solid #ddd; margin: 10px 0; padding: 15px; border-radius: 5px; background: #fafafa; }
        .document-header { font-weight: bold; color: #007acc; margin-bottom: 10px; cursor: pointer; text-decoration: underline; }
        .document-header:hover { color: #005999; }
        .document-meta { font-size: 0.9em; color: #666; margin-bottom: 10px; }
        .document-preview { background: white; padding: 10px; border-left: 3px solid #007acc; font-family: monospace; white-space: pre-wrap; max-height: 200px; overflow-y: auto; font-size: 0.85em; }
        .pagination { text-align: center; margin: 20px 0; }
        .pagination button { margin: 0 5px; padding: 8px 12px; background: #f8f9fa; border: 1px solid #ddd; cursor: pointer; }
        .pagination button:hover { background: #e9ecef; }
        .pagination button.active { background: #007acc; color: white; }
        .loading { text-align: center; padding: 40px; color: #666; font-size: 1.1em; }
        .error { color: red; padding: 15px; background: #ffe6e6; border-radius: 5px; margin: 10px 0; }
        .modal { display: none; position: fixed; z-index: 1000; left: 0; top: 0; width: 100%; height: 100%; background-color: rgba(0,0,0,0.7); }
        .modal-content { background-color: white; margin: 2% auto; padding: 20px; border-radius: 8px; width: 95%; max-width: 1000px; max-height: 90%; overflow-y: auto; }
        .close { color: #aaa; float: right; font-size: 28px; font-weight: bold; cursor: pointer; line-height: 1; }
        .close:hover { color: black; }
        .full-text { font-family: 'Georgia', 'Times New Roman', serif; white-space: pre-wrap; line-height: 1.6; font-size: 1em; max-height: 500px; overflow-y: auto; padding: 15px; border: 1px solid #e0e0e0; border-radius: 5px; background: #fafafa; }
        .full-text.monospace { font-family: 'Consolas', 'Monaco', monospace; font-size: 0.9em; line-height: 1.4; }
        .no-text { color: #999; text-align: center; padding: 20px; font-style: italic; }
        .badge { display: inline-block; padding: 2px 6px; border-radius: 3px; font-size: 0.8em; margin-left: 5px; }
        .badge.success { background: #d4edda; color: #155724; }
        .badge.danger { background: #f8d7da; color: #721c24; }
        .text-controls { margin-bottom: 15px; display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
        .text-controls button { background: #28a745; color: white; border: none; padding: 8px 15px; border-radius: 4px; cursor: pointer; font-size: 14px; transition: background-color 0.2s; }
        .text-controls button:hover { background: #218838; }
        .text-controls button.secondary { background: #6c757d; }
        .text-controls button.secondary:hover { background: #5a6268; }
        .text-controls button.warning { background: #ffc107; color: #000; }
        .text-controls button.warning:hover { background: #e0a800; }
        .text-controls button.danger { background: #dc3545; }
        .text-controls button.danger:hover { background: #c82333; }
        .text-controls button.primary { background: #007bff; }
        .text-controls button.primary:hover { background: #0056b3; }
        .text-controls .review-flag { padding: 5px 10px; background: #fff3cd; color: #856404; border: 1px solid #ffeaa7; border-radius: 4px; font-size: 0.9em; }
        .edit-textarea { width: 100%; min-height: 500px; font-family: 'Georgia', 'Times New Roman', serif; font-size: 1em; line-height: 1.6; padding: 15px; border: 2px solid #007bff; border-radius: 5px; resize: vertical; }
        .save-info { color: #666; font-size: 0.85em; margin-top: 10px; font-style: italic; }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🔍 JumpChain Text Browser</h1>
            <p>Browse and search through extracted document text from your JumpChain collection</p>
        </div>
        <div class=""controls"">
            <input type=""text"" id=""searchBox"" placeholder=""Search in document names or text content..."" style=""width: 300px;"">
            <select id=""sortBy"">
                <option value=""id"">Sort by ID</option>
                <option value=""name"">Sort by Name</option>
                <option value=""textlength"">Sort by Text Length</option>
                <option value=""method"">Sort by Extraction Method</option>
            </select>
            <select id=""sortOrder"">
                <option value=""asc"">Ascending</option>
                <option value=""desc"">Descending</option>
            </select>
            <select id=""hasText"">
                <option value="""">All Documents</option>
                <option value=""true"">With Text Only</option>
                <option value=""false"">Without Text Only</option>
            </select>
            <select id=""extractionMethod"">
                <option value="""">All Methods</option>
                <option value=""improved_pdfpig"">Improved PdfPig</option>
                <option value=""basic_pdfpig"">Basic PdfPig</option>
                <option value=""drive_export"">Drive Export</option>
            </select>
            <button onclick=""loadDocuments(1)"">Search</button>
            <button onclick=""resetFilters()"">Reset</button>
        </div>
        <div id=""statsArea""></div>
        <div id=""documentsArea"" class=""loading"">Loading documents...</div>
        <div id=""paginationArea""></div>
    </div>
    <div id=""textModal"" class=""modal"">
        <div class=""modal-content"">
            <span class=""close"" onclick=""closeModal()"">&times;</span>
            <div id=""modalContent""></div>
        </div>
    </div>
    <script>
        let currentPage = 1;
        const limit = 10;
        let isAdmin = false;
        let isEditMode = false;
        let currentDocumentId = null;
        
        async function checkAdminStatus() {
            try {
                const response = await fetch('/api/browser/check-admin');
                const data = await response.json();
                isAdmin = data.isAdmin;
                return isAdmin;
            } catch {
                return false;
            }
        }
        
        async function loadDocuments(page = 1) {
            console.log('loadDocuments called with page:', page);
            currentPage = page;
            const search = document.getElementById('searchBox').value;
            const sortBy = document.getElementById('sortBy').value;
            const sortOrder = document.getElementById('sortOrder').value;
            const hasTextValue = document.getElementById('hasText').value;
            const extractionMethod = document.getElementById('extractionMethod').value;
            const params = new URLSearchParams({ page: page.toString(), limit: limit.toString(), search, sortBy, sortOrder, extractionMethod });
            if (hasTextValue && hasTextValue !== '') { params.append('hasText', hasTextValue); }
            document.getElementById('documentsArea').innerHTML = '<div class=""loading"">Loading documents...</div>';
            try {
                const url = `/api/browser/browse?${params}`;
                console.log('Fetching:', url);
                const response = await fetch(url);
                const data = await response.json();
                console.log('Received data:', data);
                if (data.success) { displayStats(data); displayDocuments(data.documents); displayPagination(data); }
                else { document.getElementById('documentsArea').innerHTML = `<div class=""error"">Error: ${data.error || 'Unknown error occurred'}</div>`; }
            } catch (error) { 
                console.error('Error loading documents:', error);
                document.getElementById('documentsArea').innerHTML = `<div class=""error"">Network error: ${error.message}</div>`; 
            }
        }
        function displayStats(data) {
            document.getElementById('statsArea').innerHTML = `<div class=""stats"">📊 <strong>${data.totalCount.toLocaleString()}</strong> documents found | Page <strong>${data.page}</strong> of <strong>${data.totalPages}</strong> | Showing <strong>${data.documents.length}</strong> documents</div>`;
        }
        function displayDocuments(documents) {
            console.log('displayDocuments called with', documents.length, 'documents');
            if (documents.length === 0) { document.getElementById('documentsArea').innerHTML = '<div class=""no-text"">No documents found matching your criteria.</div>'; return; }
            try {
                const html = documents.map(doc => `<div class=""document-card""><div class=""document-header"" onclick=""viewFullText(${doc.id})"">📄 ${escapeHtml(doc.name || 'Untitled')} (ID: ${doc.id}) ${doc.hasExtractedText ? '<span class=""badge success"">✅ Has Text</span>' : '<span class=""badge danger"">❌ No Text</span>'}</div><div class=""document-meta"">📁 <strong>Folder:</strong> ${escapeHtml(doc.folderPath || 'Root')} | 💾 <strong>Size:</strong> ${formatFileSize(doc.size)} | 📝 <strong>Text:</strong> ${doc.extractedTextLength.toLocaleString()} chars | 📖 <strong>Words:</strong> ${doc.wordCount.toLocaleString()} | 🔧 <strong>Method:</strong> ${escapeHtml(doc.extractionMethod || 'None')}</div>${doc.extractedTextPreview ? `<div class=""document-preview"">${escapeHtml(doc.extractedTextPreview)}</div>` : '<div class=""no-text"">No extracted text available - click to try viewing anyway</div>'}</div>`).join('');
                document.getElementById('documentsArea').innerHTML = html;
                console.log('Documents displayed successfully');
            } catch (error) {
                console.error('Error in displayDocuments:', error);
                document.getElementById('documentsArea').innerHTML = '<div class=""error"">Error displaying documents</div>';
            }
        }
        function displayPagination(data) {
            if (data.totalPages <= 1) { document.getElementById('paginationArea').innerHTML = ''; return; }
            let html = '<div class=""pagination"">';
            if (data.hasPrevious) { html += `<button onclick=""loadDocuments(1)"">First</button>`; html += `<button onclick=""loadDocuments(${data.page - 1})"">Previous</button>`; }
            const startPage = Math.max(1, data.page - 2); const endPage = Math.min(data.totalPages, data.page + 2);
            for (let i = startPage; i <= endPage; i++) { if (i === data.page) { html += `<button class=""active"">${i}</button>`; } else { html += `<button onclick=""loadDocuments(${i})"">${i}</button>`; } }
            if (data.hasNext) { html += `<button onclick=""loadDocuments(${data.page + 1})"">Next</button>`; html += `<button onclick=""loadDocuments(${data.totalPages})"">Last</button>`; }
            html += '</div>'; document.getElementById('paginationArea').innerHTML = html;
        }
        async function viewFullText(documentId) {
            console.log('viewFullText called with documentId:', documentId);
            currentDocumentId = documentId;
            isEditMode = false;
            document.getElementById('modalContent').innerHTML = '<div class=""loading"">Loading document...</div>';
            document.getElementById('textModal').style.display = 'block';
            await checkAdminStatus();
            try {
                const url = `/api/browser/browse/${documentId}`;
                console.log('Fetching document:', url);
                const response = await fetch(url);
                const data = await response.json();
                console.log('Document data:', data);
                if (data.success) {
                    const doc = data.document;
                    window.originalText = doc.extractedText;
                    
                    let reviewFlagHtml = '';
                    if (doc.textNeedsReview) {
                        reviewFlagHtml = `<span class=""review-flag"">⚠️ Flagged for review by ${escapeHtml(doc.textReviewFlaggedBy || 'user')} on ${new Date(doc.textReviewFlaggedAt).toLocaleString()}</span>`;
                    }
                    
                    let editInfoHtml = '';
                    if (doc.textLastEditedAt) {
                        editInfoHtml = `<div class=""save-info"">✏️ Last edited by ${escapeHtml(doc.textLastEditedBy || 'admin')} on ${new Date(doc.textLastEditedAt).toLocaleString()}</div>`;
                    }
                    
                    document.getElementById('modalContent').innerHTML = `<h2>📄 ${escapeHtml(doc.name || 'Untitled')}</h2><div class=""document-meta"" style=""margin-bottom: 15px; padding: 10px; background: #f8f9fa; border-radius: 5px;""><strong>ID:</strong> ${doc.id} | <strong>Size:</strong> ${formatFileSize(doc.size)} | <strong>Text Length:</strong> ${doc.extractedTextLength.toLocaleString()} chars | <strong>Words:</strong> ${doc.wordCount.toLocaleString()} | <strong>Lines:</strong> ${doc.lineCount.toLocaleString()}<br><strong>Folder:</strong> ${escapeHtml(doc.folderPath || 'Root')}<br><strong>Drive:</strong> ${escapeHtml(doc.sourceDrive || 'Unknown')} | <strong>Type:</strong> ${escapeHtml(doc.mimeType || 'Unknown')} | <strong>Method:</strong> ${escapeHtml(doc.extractionMethod || 'None')}</div>${doc.extractedText ? `<div class=""text-controls""><button id=""cleanTextBtn"" onclick=""toggleCleanText(${doc.id})"" title=""Remove unnecessary line breaks and clean up formatting"">🧹 Clean Text</button><button id=""fontToggleBtn"" onclick=""toggleFont()"" class=""secondary"" title=""Switch between serif and monospace font"">🔤 Toggle Font</button>${isAdmin ? `<button id=""editBtn"" onclick=""toggleEditMode()"" class=""primary"" title=""Edit the extracted text"">✏️ Edit</button>` : ''}<button id=""flagBtn"" onclick=""toggleReviewFlag(${doc.id}, ${doc.textNeedsReview})"" class=""${doc.textNeedsReview ? 'danger' : 'warning'}"" title=""${doc.textNeedsReview ? 'Remove review flag' : 'Flag this text for admin review'}"">${doc.textNeedsReview ? '✓ Unflag' : '⚠️ Needs Review'}</button>${reviewFlagHtml}</div><div id=""textContainer"" class=""full-text"">${escapeHtml(doc.extractedText)}</div>${editInfoHtml}` : '<div class=""no-text"">No extracted text available for this document.</div>'}`;
                    console.log('Modal content updated successfully');
                } else { 
                    console.error('API returned error:', data.message);
                    document.getElementById('modalContent').innerHTML = `<div class=""error"">Error loading document: ${data.message || 'Unknown error'}</div>`; 
                }
            } catch (error) { 
                console.error('Error in viewFullText:', error);
                document.getElementById('modalContent').innerHTML = `<div class=""error"">Network error: ${error.message}</div>`; 
            }
        }
        
        async function toggleReviewFlag(documentId, currentlyFlagged) {
            const endpoint = currentlyFlagged ? 'unflag-review' : 'flag-review';
            try {
                const response = await fetch(`/api/browser/${endpoint}/${documentId}`, { method: 'POST' });
                const data = await response.json();
                if (data.success) {
                    await viewFullText(documentId);
                } else {
                    alert('Error: ' + data.message);
                }
            } catch (error) {
                alert('Network error: ' + error.message);
            }
        }
        
        function toggleEditMode() {
            if (!isAdmin) {
                alert('Admin access required to edit text');
                return;
            }
            
            const textContainer = document.getElementById('textContainer');
            const editBtn = document.getElementById('editBtn');
            
            if (!isEditMode) {
                const currentText = window.originalText || textContainer.textContent;
                textContainer.innerHTML = `<textarea id=""editTextArea"" class=""edit-textarea"">${escapeHtml(currentText)}</textarea><div style=""margin-top: 10px;""><button onclick=""saveEditedText()"" style=""background: #007bff; color: white; border: none; padding: 10px 20px; font-size: 16px; border-radius: 4px; cursor: pointer;"">💾 Save Changes</button><button onclick=""cancelEdit()"" style=""background: #6c757d; color: white; border: none; padding: 10px 20px; font-size: 16px; border-radius: 4px; cursor: pointer; margin-left: 10px;"">❌ Cancel</button></div>`;
                editBtn.textContent = '👁️ View Mode';
                editBtn.style.background = '#6c757d';
                isEditMode = true;
            } else {
                viewFullText(currentDocumentId);
            }
        }
        
        function cancelEdit() {
            viewFullText(currentDocumentId);
        }
        
        async function saveEditedText() {
            const textarea = document.getElementById('editTextArea');
            if (!textarea) return;
            
            const newText = textarea.value;
            
            if (!confirm(""Save changes to this document's extracted text?"")) {
                return;
            }
            
            try {
                const response = await fetch(`/api/browser/save-text/${currentDocumentId}`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ text: newText })
                });
                
                const data = await response.json();
                if (data.success) {
                    alert('Text saved successfully!');
                    await viewFullText(currentDocumentId);
                } else {
                    alert('Error saving text: ' + (data.error || 'Unknown error'));
                }
            } catch (error) {
                alert('Network error: ' + error.message);
            }
        }
        function closeModal() { document.getElementById('textModal').style.display = 'none'; }
        function resetFilters() { document.getElementById('searchBox').value = ''; document.getElementById('sortBy').value = 'id'; document.getElementById('sortOrder').value = 'asc'; document.getElementById('hasText').value = ''; document.getElementById('extractionMethod').value = ''; loadDocuments(1); }
        function formatFileSize(bytes) { if (bytes === 0) return '0 B'; const k = 1024; const sizes = ['B', 'KB', 'MB', 'GB']; const i = Math.floor(Math.log(bytes) / Math.log(k)); return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]; }
        function escapeHtml(text) { if (!text) return ''; const div = document.createElement('div'); div.textContent = text; return div.innerHTML; }
        function cleanText(text) { if (!text) return ''; const lines = text.split('\n'); const cleaned = []; let currentParagraph = ''; for (let i = 0; i < lines.length; i++) { const line = lines[i].trim(); if (line === '') { if (currentParagraph.trim()) { cleaned.push(currentParagraph.trim()); currentParagraph = ''; } cleaned.push(''); continue; } const isNewParagraph = (/^(Chapter|Section|\d+\.|•|-)/.test(line) || (currentParagraph && /[.!?:]$/.test(currentParagraph.trim())) || line.length < 50 && /^[A-Z]/.test(line) && !/[.!?]$/.test(line) || (/^[A-Z]/.test(line) && currentParagraph && !/[,;]$/.test(currentParagraph.trim()))); if (isNewParagraph && currentParagraph.trim()) { cleaned.push(currentParagraph.trim()); currentParagraph = line; } else { if (currentParagraph) { const needsSpace = !currentParagraph.endsWith('-') && !line.startsWith('-'); currentParagraph += (needsSpace ? ' ' : '') + line; } else { currentParagraph = line; } } } if (currentParagraph.trim()) { cleaned.push(currentParagraph.trim()); } return cleaned.join('\n'); }
        function toggleCleanText(documentId) { const textDiv = document.querySelector('.full-text'); const toggleBtn = document.getElementById('cleanTextBtn'); if (!textDiv || !toggleBtn) return; const isCurrentlyCleaned = toggleBtn.textContent.includes('Show Original'); if (isCurrentlyCleaned) { textDiv.innerHTML = escapeHtml(window.originalText); toggleBtn.textContent = '🧹 Clean Text'; toggleBtn.title = 'Remove unnecessary line breaks and clean up formatting'; } else { if (!window.originalText) { window.originalText = textDiv.textContent; } const cleanedText = cleanText(window.originalText); textDiv.innerHTML = escapeHtml(cleanedText); toggleBtn.textContent = '📄 Show Original'; toggleBtn.title = 'Show the original extracted text with all line breaks'; } }
        function toggleFont() { const textDiv = document.querySelector('.full-text'); const fontBtn = document.getElementById('fontToggleBtn'); if (!textDiv || !fontBtn) return; const isMonospace = textDiv.classList.contains('monospace'); if (isMonospace) { textDiv.classList.remove('monospace'); fontBtn.textContent = '🔤 Monospace'; fontBtn.title = 'Switch to monospace font'; } else { textDiv.classList.add('monospace'); fontBtn.textContent = '🔤 Serif'; fontBtn.title = 'Switch to serif font'; } }
        document.getElementById('searchBox').addEventListener('keypress', function(e) { if (e.key === 'Enter') { loadDocuments(1); } });
        window.onclick = function(event) { const modal = document.getElementById('textModal'); if (event.target === modal) { closeModal(); } }
        window.onload = function() { 
            loadDocuments(1); 
            // Check if we should auto-open a specific document
            const urlParams = new URLSearchParams(window.location.search);
            const docId = urlParams.get('docId');
            if (docId) {
                setTimeout(() => viewFullText(parseInt(docId)), 500);
            }
        };
    </script>
</body>
</html>";
    }

    private static string GetParserTestHtml()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Purchasable Parser Test</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .container { max-width: 800px; margin: 0 auto; }
        button { background: #007acc; color: white; border: none; padding: 10px 15px; border-radius: 4px; cursor: pointer; margin: 5px; }
        button:hover { background: #005a9e; }
        .results { margin-top: 20px; padding: 15px; background: #f8f9fa; border-radius: 5px; max-height: 400px; overflow-y: auto; }
        .loading { color: #666; }
        .error { color: red; }
        .success { color: green; }
        pre { white-space: pre-wrap; }
    </style>
</head>
<body>
    <div class=""container"">
        <h1>🧩 Purchasable Parser Test</h1>
        <div><h3>Test Improved Parsing (Preview Only):</h3>
            <button onclick=""testImprovedParsing(22)"">🚀 Test New Parser on Animal Well.pdf</button>
            <button onclick=""testImprovedParsing(21)"">🚀 Test New Parser on Document 21</button>
            <button onclick=""testImprovedParsing(23)"">🚀 Test New Parser on Document 23</button>
        </div>
        <div><h3>Parse & Save to Database:</h3>
            <button onclick=""parseDocument(22)"">Parse Animal Well.pdf (ID: 22)</button>
            <button onclick=""parseDocument(21)"">Parse Document ID 21</button>
            <button onclick=""parseDocument(23)"">Parse Document ID 23</button>
        </div>
        <div><h3>Batch Parse Documents:</h3>
            <button onclick=""batchParse()"">Parse 10 Documents with Text</button>
        </div>
        <div><h3>View Results:</h3>
            <button onclick=""viewPurchasables(22)"">View Animal Well Purchasables</button>
            <button onclick=""searchPurchasables()"">Search All Purchasables</button>
        </div>
        <div id=""results"" class=""results""></div>
    </div>
    <script>
        async function testImprovedParsing(documentId) { showLoading(`🚀 Testing improved parser on document ${documentId}...`); try { const response = await fetch(`/test-improved-parsing/${documentId}`, { method: 'POST' }); const data = await response.json(); showResults(JSON.stringify(data, null, 2), data.success ? 'success' : 'error'); } catch (error) { showResults(`Error: ${error.message}`, 'error'); } }
        async function parseDocument(documentId) { showLoading(`Parsing document ${documentId}...`); try { const response = await fetch(`/parse-purchasables/${documentId}`, { method: 'POST' }); const data = await response.json(); showResults(JSON.stringify(data, null, 2), data.success ? 'success' : 'error'); } catch (error) { showResults(`Error: ${error.message}`, 'error'); } }
        async function batchParse() { showLoading('Batch parsing documents with text...'); try { const response = await fetch('/parse-purchasables-batch', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ filter: 'with_text' }) }); const data = await response.json(); showResults(JSON.stringify(data, null, 2), data.success ? 'success' : 'error'); } catch (error) { showResults(`Error: ${error.message}`, 'error'); } }
        async function viewPurchasables(documentId) { showLoading(`Loading purchasables for document ${documentId}...`); try { const response = await fetch(`/purchasables/${documentId}`); const data = await response.json(); showResults(JSON.stringify(data, null, 2), data.success ? 'success' : 'error'); } catch (error) { showResults(`Error: ${error.message}`, 'error'); } }
        async function searchPurchasables() { showLoading('Searching all purchasables...'); try { const response = await fetch('/search-purchasables?limit=50'); const data = await response.json(); showResults(JSON.stringify(data, null, 2), data.success ? 'success' : 'error'); } catch (error) { showResults(`Error: ${error.message}`, 'error'); } }
        function showLoading(message) { document.getElementById('results').innerHTML = `<div class=""loading"">${message}</div>`; }
        function showResults(content, type) { const resultsDiv = document.getElementById('results'); resultsDiv.className = `results ${type}`; resultsDiv.innerHTML = `<pre>${content}</pre>`; }
    </script>
</body>
</html>";
    }
}
