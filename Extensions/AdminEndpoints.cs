using Microsoft.AspNetCore.Http.HttpResults;
using JumpChainSearch.Data;
using JumpChainSearch.Services;
using JumpChainSearch.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace JumpChainSearch.Extensions;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        // Admin Portal - Main Interface
        #pragma warning disable ASP0016 // Do not return Task<IResult> from routes - this is intentional for async HTML generation
        group.MapGet("/", GetAdminPortal);
        #pragma warning restore ASP0016
        
        // Admin API endpoints
        group.MapGet("/status", GetSystemStatus);
        group.MapGet("/batch/status", GetBatchStatus);
        group.MapPost("/batch/start", StartBatchProcessing);
        group.MapPost("/batch/stop", StopBatchProcessing);
        group.MapPost("/server/restart", RestartServer);
        group.MapGet("/batch/logs", GetBatchLogs);
        
        // Drive scanning endpoints
        group.MapGet("/drives/status", GetDriveScanStatus);
        group.MapPost("/drives/scan", StartDriveScan);
        group.MapPost("/drives/scan/stop", StopDriveScan);
        group.MapGet("/drive-configurations", GetDriveConfigurations);
        group.MapGet("/drives/{driveName}/folders", GetDriveFolders);
        group.MapPost("/drives/{driveName}/scan", ScanSingleDrive);
        group.MapPost("/drives/{driveName}/refresh-folders", RefreshDriveFolders);
        
        // Genre tagging endpoints
        group.MapPost("/tags/apply-community-genres", ApplyCommunityGenreTags);
        
        // Series tagging endpoints
        group.MapPost("/tags/apply-community-series", ApplyCommunitySeriesTags);
        
        // Tag recategorization endpoint
        group.MapGet("/tags/categories", GetTagCategories);
        group.MapGet("/tags/search", SearchTags);
        group.MapPost("/tags/recategorize", RecategorizeTag);
        
        // Text review queue endpoint
        group.MapGet("/text-review/queue", GetTextReviewQueue);
        
        // System management endpoints
        group.MapGet("/system/cache-ttl", GetCacheTTL);
        group.MapPost("/system/cache-ttl", UpdateCacheTTL);
        #pragma warning disable ASP0016
        group.MapPost("/system/refresh-document-count", RefreshDocumentCount).DisableAntiforgery();
        #pragma warning restore ASP0016
        group.MapGet("/system/scan-schedule", GetScanSchedule);
        group.MapPost("/system/scan-schedule", UpdateScanSchedule);
        group.MapPost("/system/scan-schedule/set-next", SetNextScheduledScan);
        group.MapPost("/system/scan-schedule/init", InitializeScanSchedule);
        group.MapGet("/system/diagnostic", GetSystemDiagnostic);
        group.MapGet("/system/series-mappings-check", CheckSeriesMappings);
        
        // Account management endpoints
        group.MapPost("/account/change-username", ChangeUsername);
        group.MapPost("/account/change-password", ChangePassword);
        group.MapPost("/logout", Logout);
        
        // OCR Analytics endpoint
        group.MapGet("/analytics/ocr-quality", GetOcrQualityAnalytics);
        
        // Batch reprocessing endpoints
        group.MapGet("/batch/reprocess-count", CalculateReprocessCount);
        group.MapGet("/batch/reprocess-progress", GetReprocessProgress);
        group.MapPost("/batch/start-reprocess", StartBatchReprocess);

        return group;
    }

    private static async Task<IResult> GetAdminPortal(HttpContext context, AdminAuthService authService, JumpChainDbContext dbContext)
    {
        // Check session authentication
        var (valid, user) = await ValidateSession(context, authService);
        
        if (!valid)
        {
            return Results.Redirect("/Admin/Login");
        }

        var username = user?.Username ?? "Admin";
        
        // Get stats for dashboard
        var totalDocuments = await dbContext.JumpDocuments.CountAsync();
        var processedDocuments = await dbContext.JumpDocuments
            .CountAsync(d => !string.IsNullOrEmpty(d.ExtractedText));
        var totalDrives = await dbContext.DriveConfigurations.CountAsync();
        
        context.Response.ContentType = "text/html";
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Admin Portal - JumpChain Search</title>
    <script src=""https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js""></script>
    <script src=""https://cdn.jsdelivr.net/npm/chartjs-plugin-datalabels@2.2.0/dist/chartjs-plugin-datalabels.min.js""></script>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        
        :root {{
            --bg-primary: #1a1a2e;
            --bg-secondary: #16213e;
            --bg-tertiary: #0f3460;
            --accent: #e94560;
            --accent-hover: #c93551;
            --text-primary: #eee;
            --text-secondary: #aaa;
            --success: #2ecc71;
            --warning: #f39c12;
            --danger: #e74c3c;
            --border: #2a2a4e;
        }}
        
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: var(--bg-primary);
            color: var(--text-primary);
            line-height: 1.6;
            min-height: 100vh;
        }}
        
        header {{
            background: var(--bg-secondary);
            border-bottom: 2px solid var(--accent);
            padding: 1rem 2rem;
            display: flex;
            justify-content: space-between;
            align-items: center;
            box-shadow: 0 2px 10px rgba(0,0,0,0.3);
        }}
        
        header h1 {{
            font-size: 1.6rem;
            color: var(--text-primary);
        }}
        
        .header-info {{
            display: flex;
            align-items: center;
            gap: 1rem;
        }}
        
        .user-info {{
            color: var(--text-secondary);
            font-size: 0.85rem;
        }}
        
        .btn {{
            padding: 0.4rem 1rem;
            background: var(--accent);
            color: white;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-size: 0.85rem;
            transition: background 0.3s, transform 0.1s;
            text-decoration: none;
            display: inline-block;
        }}
        
        .btn:hover {{
            background: var(--accent-hover);
            transform: translateY(-1px);
        }}
        
        .btn:active {{
            transform: translateY(0);
        }}
        
        .btn-secondary {{
            background: var(--bg-tertiary);
        }}
        
        .btn-secondary:hover {{
            background: #0a2540;
        }}
        
        .btn-success {{
            background: var(--success);
        }}
        
        .btn-success:hover {{
            background: #27ae60;
        }}
        
        .btn-danger {{
            background: var(--danger);
        }}
        
        .btn-danger:hover {{
            background: #c0392b;
        }}
        
        .btn-primary {{
            background: #3498db;
        }}
        
        .btn-primary:hover {{
            background: #2980b9;
        }}
        
        main {{
            max-width: 1400px;
            margin: 0 auto;
            padding: 1rem;
        }}
        
        /* Tab Navigation */
        nav {{
            background: var(--bg-secondary);
            border-radius: 8px;
            padding: 0.5rem;
            margin-bottom: 1rem;
            box-shadow: 0 2px 8px rgba(0,0,0,0.2);
        }}
        
        .tab-nav {{
            display: flex;
            gap: 0.5rem;
            flex-wrap: wrap;
        }}
        
        .tab-button {{
            position: relative;
            padding: 0.6rem 1.2rem;
            background: transparent;
            border: none;
            color: var(--text-secondary);
            cursor: pointer;
            border-radius: 6px;
            font-size: 0.9rem;
            transition: all 0.3s;
            display: flex;
            align-items: center;
            gap: 0.5rem;
        }}
        
        .tab-button:hover {{
            background: var(--bg-tertiary);
            color: var(--text-primary);
        }}
        
        .tab-button.active {{
            background: var(--accent);
            color: white;
        }}
        
        .badge {{
            display: inline-block;
            min-width: 18px;
            height: 18px;
            padding: 0 5px;
            background: var(--danger);
            color: white;
            border-radius: 9px;
            font-size: 0.7rem;
            font-weight: bold;
            line-height: 18px;
            text-align: center;
        }}
        
        .badge.success {{
            background: var(--success);
        }}
        
        /* Stats Dashboard */
        .stats-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 1rem;
            margin-bottom: 1rem;
        }}
        
        .stat-card {{
            background: var(--bg-secondary);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 1rem;
            box-shadow: 0 2px 6px rgba(0,0,0,0.2);
        }}
        
        .stat-card h3 {{
            color: var(--text-secondary);
            font-size: 0.8rem;
            margin-bottom: 0.3rem;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }}
        
        .stat-value {{
            font-size: 2rem;
            font-weight: bold;
            color: var(--accent);
        }}
        
        /* Tab Content Sections */
        .tab-content {{
            display: none;
        }}
        
        .tab-content.active {{
            display: block;
        }}
        
        section {{
            background: var(--bg-secondary);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 1.5rem;
            margin-bottom: 1rem;
            box-shadow: 0 2px 6px rgba(0,0,0,0.2);
        }}
        
        section h2 {{
            color: var(--text-primary);
            margin-bottom: 1rem;
            font-size: 1.3rem;
            border-bottom: 2px solid var(--accent);
            padding-bottom: 0.5rem;
        }}
        
        .action-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
            gap: 1rem;
        }}
        
        .action-card {{
            background: var(--bg-tertiary);
            border: 1px solid var(--border);
            border-radius: 6px;
            padding: 1rem;
        }}
        
        .action-card h3 {{
            color: var(--text-primary);
            margin-bottom: 0.5rem;
            font-size: 1.1rem;
        }}
        
        .action-card p {{
            color: var(--text-secondary);
            font-size: 0.85rem;
            margin-bottom: 0.8rem;
        }}
        
        .status {{
            display: inline-block;
            padding: 0.25rem 0.75rem;
            border-radius: 15px;
            font-size: 0.8rem;
            font-weight: bold;
            margin-bottom: 0.5rem;
        }}
        
        .status-running {{
            background: var(--success);
            color: white;
        }}
        
        .status-stopped {{
            background: var(--danger);
            color: white;
        }}
        
        .status-idle {{
            background: var(--text-secondary);
            color: white;
        }}
        
        .log-container {{
            background: #0a0a0a;
            border: 1px solid var(--border);
            border-radius: 5px;
            padding: 1rem;
            max-height: 300px;
            overflow-y: auto;
            font-family: 'Courier New', monospace;
            font-size: 0.85rem;
            color: #0f0;
            margin-top: 1rem;
        }}
        
        .spinner {{
            display: inline-block;
            width: 14px;
            height: 14px;
            border: 2px solid var(--text-secondary);
            border-top: 2px solid var(--accent);
            border-radius: 50%;
            animation: spin 1s linear infinite;
        }}
        
        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}
        
        .btn-group {{
            display: flex;
            gap: 0.5rem;
            flex-wrap: wrap;
        }}
    </style>
</head>
<body>
    <header>
        <h1>🚀 JumpChain Admin Portal</h1>
        <div class=""header-info"">
            <span class=""user-info"" onclick=""switchTab('account')"" style=""cursor: pointer;"">Logged in as: <strong>{username}</strong></span>
            <form method=""post"" action=""/admin/logout"" style=""display: inline;"">
                <button type=""submit"" class=""btn btn-secondary"">Logout</button>
            </form>
        </div>
    </header>
    
    <main>
        <!-- Tab Navigation -->
        <nav>
            <div class=""tab-nav"">
                <button class=""tab-button active"" onclick=""switchTab('dashboard')"">📊 Dashboard</button>
                <button class=""tab-button"" onclick=""switchTab('processing')"">📦 Processing</button>
                <button class=""tab-button"" onclick=""switchTab('drives')"">💾 Drives</button>
                <button class=""tab-button"" onclick=""switchTab('tags')"">🏷️ Tags</button>
                <button class=""tab-button"" id=""tag-voting-tab"" onclick=""switchTab('voting')"">
                    🗳️ Tag Voting
                    <span class=""badge"" id=""voting-badge"" style=""display: none;"">0</span>
                </button>
                <button class=""tab-button"" id=""text-review-tab"" onclick=""switchTab('review')"">
                    📝 Text Review
                    <span class=""badge"" id=""review-badge"" style=""display: none;"">0</span>
                </button>
                <button class=""tab-button"" onclick=""switchTab('system')"">⚙️ System</button>
            </div>
        </nav>
        
        <!-- Dashboard Tab -->
        <div id=""dashboard"" class=""tab-content active"">
            <div class=""stats-grid"">
                <div class=""stat-card"">
                    <h3>Total Documents</h3>
                    <div class=""stat-value"" id=""total-docs"">{totalDocuments}</div>
                    <button class=""btn btn-sm btn-primary"" onclick=""refreshDocumentCount()"" 
                            style=""margin-top: 0.5rem;"" 
                            title=""Refresh the document count cache used by the front page"">
                        <i class=""fas fa-sync-alt""></i> Refresh Count
                    </button>
                </div>
                <div class=""stat-card"">
                    <h3>Processed Documents</h3>
                    <div class=""stat-value"" id=""processed-docs"">{processedDocuments}</div>
                </div>
                <div class=""stat-card"">
                    <h3>Configured Drives</h3>
                    <div class=""stat-value"" id=""total-drives"">{totalDrives}</div>
                </div>
                <div class=""stat-card"">
                    <h3>Server Status</h3>
                    <div class=""stat-value"" style=""color: var(--success);"">●</div>
                </div>
            </div>
            
            <section>
                <h2>Quick Actions</h2>
                <div class=""action-grid"">
                    <div class=""action-card"">
                        <h3>Start Drive Scan</h3>
                        <p>Scan all configured drives for new documents.</p>
                        <button class=""btn btn-success"" onclick=""switchTab('drives'); startDriveScan();"">Start Scan</button>
                    </div>
                    <div class=""action-card"">
                        <h3>Process Text</h3>
                        <p>Extract text from unprocessed documents.</p>
                        <button class=""btn btn-success"" onclick=""switchTab('processing'); startBatch();"">Start Processing</button>
                    </div>
                    <div class=""action-card"">
                        <h3>Review Queue</h3>
                        <p id=""review-queue-summary"">Checking for flagged documents...</p>
                        <button class=""btn btn-primary"" onclick=""switchTab('review')"">View Queue</button>
                    </div>
                    <div class=""action-card"">
                        <h3>Tag Voting</h3>
                        <p id=""voting-summary"">Checking for pending tags...</p>
                        <button class=""btn btn-primary"" onclick=""switchTab('voting')"">View Pending</button>
                    </div>
                </div>
            </section>
        </div>
        
        <!-- Batch Processing Tab -->
        <div id=""processing"" class=""tab-content"">
            <section>
                <h2>📦 Batch Text Extraction</h2>
                <div class=""action-card"">
                    <h3>Text Extraction Status</h3>
                    <p>Process documents to extract text content for search indexing.</p>
                    <span class=""status status-idle"" id=""batch-status"">Checking...</span>
                    <div class=""btn-group"" style=""margin-top: 1rem;"">
                        <button class=""btn btn-success"" onclick=""startBatch()"">Start Processing</button>
                        <button class=""btn btn-danger"" onclick=""stopBatch()"">Stop</button>
                    </div>
                    <div id=""batch-info"" style=""margin-top: 1rem; color: var(--text-secondary); font-size: 0.85rem;""></div>
                </div>
            </section>
            
            <section style=""margin-top: 2rem;"">
                <h2>📊 OCR Quality Analytics</h2>
                <p style=""color: var(--text-secondary); margin-bottom: 1rem;"">
                    Analyze text extraction quality and identify documents that may need review or re-processing.
                </p>
                
                <div style=""text-align: center; margin-bottom: 1rem;"">
                    <button class=""btn btn-primary"" onclick=""loadOcrAnalytics()"">Load Analytics</button>
                </div>
                
                <div id=""analytics-loading"" style=""display: none; text-align: center; color: var(--text-secondary); padding: 2rem;"">
                    <div class=""spinner"" style=""margin: 0 auto 1rem;""></div>
                    <p>Loading analytics data...</p>
                </div>
                
                <div id=""analytics-charts"" style=""display: none;"">
                    <div style=""display: grid; grid-template-columns: repeat(auto-fit, minmax(450px, 1fr)); gap: 2rem;"">
                        <div class=""action-card"">
                            <h3>Document Count by Text Length</h3>
                            <p style=""font-size: 0.85rem; color: var(--text-secondary); margin-bottom: 1rem;"">
                                Distribution of extracted text sizes. Very short text (&lt;100 chars) may indicate extraction failures. Click a bar to search those documents.
                            </p>
                            <canvas id=""text-length-chart"" style=""max-height: 300px; cursor: pointer;""></canvas>
                        </div>
                        
                        <div class=""action-card"">
                            <h3>Document Count by OCR Quality</h3>
                            <p style=""font-size: 0.85rem; color: var(--text-secondary); margin-bottom: 1rem;"">
                                OCR confidence scores. Quality &lt; 0.5 is flagged as low quality and may need review. Click a bar to search those documents.
                            </p>
                            <canvas id=""ocr-quality-chart"" style=""max-height: 300px; cursor: pointer;""></canvas>
                        </div>
                    </div>
                    
                    <div style=""margin-top: 1rem; padding: 1rem; background: var(--bg-tertiary); border-radius: 6px;"">
                        <h4 style=""margin-bottom: 0.5rem; color: var(--text-primary);"">Summary Statistics</h4>
                        <div style=""display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1rem; font-size: 0.9rem; color: var(--text-secondary);"">
                            <div>Total Documents: <strong id=""total-docs-stat"">-</strong></div>
                            <div>Very Short Text (&lt;100 chars): <strong id=""short-text-stat"">-</strong></div>
                            <div>OCR Documents: <strong id=""ocr-docs-stat"">-</strong></div>
                            <div>Low Quality OCR (&lt;0.5): <strong id=""low-quality-stat"">-</strong></div>
                        </div>
                    </div>
                    
                    <div style=""margin-top: 2rem; padding: 1.5rem; background: var(--bg-tertiary); border-radius: 6px; border: 2px solid var(--accent);"">
                        <h4 style=""margin-bottom: 1rem; color: var(--accent);"">🔄 Batch Reprocessing Controls</h4>
                        <p style=""font-size: 0.9rem; color: var(--text-secondary); margin-bottom: 1rem;"">
                            Reprocess documents with low quality metrics to attempt improved text extraction.
                        </p>
                        
                        <div style=""display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 1.5rem;"">
                            <div>
                                <label style=""display: block; margin-bottom: 0.5rem; color: var(--text-primary);"">
                                    Text Length Threshold (characters)
                                </label>
                                <input type=""number"" id=""reprocess-text-threshold"" value=""100"" min=""0"" max=""10000"" step=""50""
                                       style=""width: 100%; padding: 0.5rem; background: var(--bg-primary); border: 1px solid var(--border); color: var(--text-primary); border-radius: 4px;"" />
                                <small style=""color: var(--text-secondary);"">Reprocess documents with ≤ this many characters</small>
                            </div>
                            
                            <div>
                                <label style=""display: block; margin-bottom: 0.5rem; color: var(--text-primary);"">
                                    OCR Quality Threshold
                                </label>
                                <input type=""number"" id=""reprocess-quality-threshold"" value=""0.5"" min=""0"" max=""1"" step=""0.1""
                                       style=""width: 100%; padding: 0.5rem; background: var(--bg-primary); border: 1px solid var(--border); color: var(--text-primary); border-radius: 4px;"" />
                                <small style=""color: var(--text-secondary);"">Reprocess OCR documents with ≤ this quality score</small>
                            </div>
                        </div>
                        
                        <div style=""margin-top: 1.5rem; display: flex; gap: 1rem; align-items: center; flex-wrap: wrap;"">
                            <button class=""btn btn-warning"" onclick=""calculateReprocessCount()"">Calculate Affected Documents</button>
                            <button class=""btn btn-danger"" id=""start-reprocess-btn"" onclick=""startReprocessing()"" disabled>
                                Start Reprocessing (<span id=""reprocess-count"">?</span> documents)
                            </button>
                            <span id=""reprocess-status"" style=""color: var(--text-secondary); font-size: 0.9rem;""></span>
                        </div>
                    </div>
                </div>
            </section>
        </div>
        
        <!-- Drive Scanning Tab -->
        <div id=""drives"" class=""tab-content"">
            <section>
                <h2>💾 Google Drive Management</h2>
                <div class=""action-card"">
                    <h3>Drive Sync Status</h3>
                    <p>Scan configured Google Drives for new JumpChain documents.</p>
                    <span class=""status status-idle"" id=""drive-status"">Checking...</span>
                    <div class=""btn-group"" style=""margin-top: 1rem;"">
                        <button class=""btn btn-success"" onclick=""startDriveScan()"">Scan All Drives</button>
                        <button class=""btn btn-danger"" onclick=""stopDriveScan()"">Stop</button>
                        <button class=""btn btn-primary"" onclick=""loadDriveList()"">Refresh Drive List</button>
                    </div>
                    <div id=""drive-info"" style=""margin-top: 1rem; color: var(--text-secondary); font-size: 0.85rem;""></div>
                </div>
                
                <div id=""drive-list"" style=""margin-top: 1.5rem;"">
                    <p style=""color: var(--text-secondary);"">Click ""Refresh Drive List"" to load drives...</p>
                </div>
            </section>
        </div>
        
        <!-- Genre Tagging Tab -->
        <div id=""tags"" class=""tab-content"">
            <section>
                <h2>🏷️ Tag Management</h2>
                <div class=""action-grid"">
                    <div class=""action-card"">
                        <h3>Community Genre Tags</h3>
                        <p>Apply genre tags from the community tag list to matching documents.</p>
                        <button class=""btn btn-success"" onclick=""applyGenreTags()"">Apply Genre Tags</button>
                        <div id=""genre-info"" style=""margin-top: 1rem; color: var(--text-secondary); font-size: 0.85rem;""></div>
                    </div>
                    <div class=""action-card"">
                        <h3>Community Series Tags</h3>
                        <p>Apply series tags from the community series list to matching documents.</p>
                        <button class=""btn btn-success"" onclick=""applySeriesTags()"">Apply Series Tags</button>
                        <div id=""series-info"" style=""margin-top: 1rem; color: var(--text-secondary); font-size: 0.85rem;""></div>
                    </div>
                </div>
            </section>
            
            <section style=""margin-top: 2rem; border-top: 2px solid var(--border); padding-top: 2rem;"">
                <h2>🔄 Recategorize Tags</h2>
                <p style=""color: var(--text-secondary); margin-bottom: 1rem;"">
                    Change the category of a tag across all documents. Associated approval rules will be updated automatically.
                </p>
                <div class=""action-card"">
                    <div style=""display: grid; grid-template-columns: 2fr 1fr auto; gap: 1rem; align-items: start;"">
                        <div style=""position: relative;"">
                            <input type=""text"" id=""tag-search-input"" placeholder=""Search tags (case-insensitive)..."" 
                                   oninput=""searchTagsDebounced(this.value)""
                                   autocomplete=""off""
                                   style=""width: 100%; padding: 0.75rem; border: 1px solid var(--border); border-radius: 4px; background: var(--bg); font-size: 1rem;"" />
                            <div id=""tag-search-results"" style=""position: absolute; width: 100%; max-height: 300px; overflow-y: auto; background: var(--bg-secondary); border: 1px solid var(--border); border-top: none; border-radius: 0 0 4px 4px; display: none; z-index: 1000;""></div>
                        </div>
                        
                        <select id=""new-category-select"" disabled style=""width: 100%; padding: 0.75rem; border: 1px solid var(--border); border-radius: 4px; background: var(--bg); font-size: 1rem;"">
                            <option value="""">Select tag first...</option>
                            <!-- Categories loaded dynamically -->
                        </select>
                        
                        <button class=""btn btn-primary"" onclick=""recategorizeSelectedTag()"" disabled id=""recategorize-btn"" style=""white-space: nowrap; padding: 0.75rem 1.5rem;"">
                            Recategorize
                        </button>
                    </div>
                    <div id=""selected-tag-info"" style=""display: none; margin-top: 0.5rem; padding: 0.5rem; color: var(--text-secondary); font-size: 0.9rem;"">
                        <strong id=""selected-tag-name"" style=""color: var(--accent);""></strong> · 
                        <span id=""selected-tag-category""></span> · 
                        <span id=""selected-tag-count""></span> documents
                    </div>
                    <div id=""recategorize-result"" style=""margin-top: 0.5rem; color: var(--text-secondary); font-size: 0.9rem;""></div>
                </div>
            </section>
        </div>
        
        <!-- Tag Voting Tab -->
        <div id=""voting"" class=""tab-content"">
            <section>
                <h2>🗳️ Tag Recommendations</h2>
                <div class=""action-grid"">
                    <div class=""action-card"">
                        <h3>Pending Tag Suggestions</h3>
                        <p>Review and approve/reject community tag suggestions.</p>
                        <button class=""btn btn-primary"" onclick=""loadPendingTags()"">Load Pending Tags</button>
                    </div>
                    <div class=""action-card"">
                        <h3>Voting Configuration</h3>
                        <p>Configure automatic approval thresholds.</p>
                        <button class=""btn btn-secondary"" onclick=""showVotingConfig()"">Configure</button>
                    </div>
                </div>
                <div id=""pending-tags-info"" style=""margin-top: 1rem;""></div>
            </section>
            
            <section style=""margin-top: 2rem; border-top: 2px solid var(--border); padding-top: 2rem;"">
                <h2>🔗 Tag Relationships</h2>
                <p style=""color: var(--text-secondary); margin-bottom: 1rem;"">Define parent-child relationships between tags for hierarchical organization.</p>
                
                <div class=""action-card"" style=""margin-bottom: 2rem;"">
                    <h3>Add Tag Relationship</h3>
                    <div style=""display: grid; grid-template-columns: 2fr auto 2fr auto; gap: 1rem; align-items: end; margin-top: 1rem;"">
                        <div>
                            <label style=""display: block; margin-bottom: 0.5rem; font-weight: 500;"">Parent Tag</label>
                            <input type=""text"" id=""parent-tag"" placeholder=""e.g., Star Wars"" 
                                   style=""width: 100%; padding: 0.5rem; border: 1px solid var(--border); border-radius: 4px; background: var(--bg);"" />
                        </div>
                        <div style=""text-align: center; padding-top: 1.5rem;"">
                            <i class=""fas fa-arrow-right"" style=""color: var(--accent); font-size: 1.5rem;""></i>
                        </div>
                        <div>
                            <label style=""display: block; margin-bottom: 0.5rem; font-weight: 500;"">Child Tag</label>
                            <input type=""text"" id=""child-tag"" placeholder=""e.g., Prequel Trilogy"" 
                                   style=""width: 100%; padding: 0.5rem; border: 1px solid var(--border); border-radius: 4px; background: var(--bg);"" />
                        </div>
                        <button onclick=""addTagHierarchy()"" class=""btn btn-primary"" style=""white-space: nowrap;"">
                            <i class=""fas fa-plus""></i> Add Relationship
                        </button>
                    </div>
                </div>
                
                <div class=""action-card"">
                    <div style=""display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem;"">
                        <h3 style=""margin: 0;"">Existing Relationships</h3>
                        <button class=""btn btn-secondary"" onclick=""loadTagHierarchies()"">
                            <i class=""fas fa-sync""></i> Refresh
                        </button>
                    </div>
                    <div id=""hierarchy-list"" style=""margin-top: 1rem;"">
                        <p style=""color: var(--text-secondary);"">Click Refresh to load existing tag relationships</p>
                    </div>
                </div>
            </section>
        </div>
        
        <!-- Text Review Tab -->
        <div id=""review"" class=""tab-content"">
            <section>
                <h2>📝 Text Review Queue</h2>
                <div class=""action-card"">
                    <h3>Documents Needing Review</h3>
                    <p>Documents flagged by users for text extraction errors.</p>
                    <button class=""btn btn-primary"" onclick=""loadReviewQueue()"">Load Review Queue</button>
                    <div id=""review-queue"" style=""margin-top: 1rem;""></div>
                </div>
            </section>
        </div>
        
        <!-- Account Management Tab -->
        <div id=""account"" class=""tab-content"">
            <section>
                <h2>👤 Account Management</h2>
                <div class=""action-grid"">
                    <div class=""action-card"">
                        <h3>Change Username</h3>
                        <p>Update your administrator username.</p>
                        <form onsubmit=""changeUsername(event)"">
                            <input type=""text"" id=""new-username"" placeholder=""New Username"" required style=""width: 100%; padding: 0.5rem; margin: 0.5rem 0; border: 1px solid var(--border); border-radius: 4px; background: var(--bg-secondary); color: var(--text-primary);"" />
                            <input type=""password"" id=""username-current-password"" placeholder=""Current Password"" required style=""width: 100%; padding: 0.5rem; margin: 0.5rem 0; border: 1px solid var(--border); border-radius: 4px; background: var(--bg-secondary); color: var(--text-primary);"" />
                            <button type=""submit"" class=""btn btn-primary"">Update Username</button>
                        </form>
                        <div id=""username-result"" style=""margin-top: 0.5rem; color: var(--text-secondary); font-size: 0.9rem;""></div>
                    </div>
                    <div class=""action-card"">
                        <h3>Change Password</h3>
                        <p>Update your administrator password.</p>
                        <form onsubmit=""changePassword(event)"">
                            <input type=""password"" id=""current-password"" placeholder=""Current Password"" required style=""width: 100%; padding: 0.5rem; margin: 0.5rem 0; border: 1px solid var(--border); border-radius: 4px; background: var(--bg-secondary); color: var(--text-primary);"" />
                            <input type=""password"" id=""new-password"" placeholder=""New Password (min 8 characters)"" required minlength=""8"" style=""width: 100%; padding: 0.5rem; margin: 0.5rem 0; border: 1px solid var(--border); border-radius: 4px; background: var(--bg-secondary); color: var(--text-primary);"" />
                            <input type=""password"" id=""confirm-password"" placeholder=""Confirm New Password"" required minlength=""8"" style=""width: 100%; padding: 0.5rem; margin: 0.5rem 0; border: 1px solid var(--border); border-radius: 4px; background: var(--bg-secondary); color: var(--text-primary);"" />
                            <button type=""submit"" class=""btn btn-primary"">Update Password</button>
                        </form>
                        <div id=""password-result"" style=""margin-top: 0.5rem; color: var(--text-secondary); font-size: 0.9rem;""></div>
                    </div>
                </div>
            </section>
        </div>
        
        <!-- System Management Tab -->
        <div id=""system"" class=""tab-content"">
            <section>
                <h2>⚙️ System Management</h2>
                <div class=""action-grid"">
                    <div class=""action-card"">
                        <h3>Server Control</h3>
                        <p>Restart the application server.</p>
                        <button class=""btn btn-danger"" onclick=""restartServer()"">Restart Server</button>
                    </div>
                    <div class=""action-card"">
                        <h3>Batch Logs</h3>
                        <p>View recent batch processing logs.</p>
                        <button class=""btn btn-secondary"" onclick=""viewLogs()"">View Logs</button>
                    </div>
                </div>
                <div id=""logs"" class=""log-container"" style=""display: none;""></div>
            </section>
            
            <section style=""margin-top: 2rem; border-top: 2px solid var(--border); padding-top: 2rem;"">
                <h2>💾 Cache Management</h2>
                <p style=""color: var(--text-secondary); margin-bottom: 1rem;"">
                    Configure how long search results are cached to balance performance and freshness.
                </p>
                <div class=""action-card"">
                    <h3>Search Cache TTL</h3>
                    <div style=""display: flex; gap: 1rem; align-items: center; margin-top: 1rem;"">
                        <label style=""font-weight: 500;"">Cache Duration (minutes):</label>
                        <input type=""number"" id=""cache-ttl"" value=""5"" min=""1"" max=""60"" 
                               style=""width: 80px; padding: 0.5rem; border: 1px solid var(--border); border-radius: 4px; background: var(--bg);"" />
                        <button class=""btn btn-primary"" onclick=""updateCacheTTL()"">Update Cache TTL</button>
                        <span id=""cache-ttl-status"" style=""color: var(--text-secondary); font-size: 0.9rem;""></span>
                    </div>
                    <div style=""margin-top: 1rem; padding: 1rem; background: var(--card-bg); border: 1px solid var(--border); border-radius: 4px;"">
                        <p style=""margin: 0; font-size: 0.85rem; color: var(--text-secondary);"">
                            <strong>Current Setting:</strong> <span id=""current-cache-ttl"">Loading...</span> minutes<br>
                            <strong>Recommended:</strong> 5-10 minutes for most use cases<br>
                            <strong>Note:</strong> Lower values provide fresher results but may impact performance
                        </p>
                    </div>
                </div>
            </section>
            
            <section style=""margin-top: 2rem; border-top: 2px solid var(--border); padding-top: 2rem;"">
                <h2>🔄 Automatic Scan Scheduling</h2>
                <p style=""color: var(--text-secondary); margin-bottom: 1rem;"">
                    Schedule automatic scans at regular intervals. When enabled, scans run continuously at the specified frequency.
                </p>
                <div class=""action-grid"">
                    <div class=""action-card"">
                        <h3>Schedule Settings</h3>
                        <div style=""margin-top: 1rem;"">
                            <label style=""display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1.5rem;"">
                                <input type=""checkbox"" id=""scan-enabled"" onchange=""toggleScanScheduling(this.checked)"" 
                                       style=""width: 20px; height: 20px;"" />
                                <span style=""font-weight: 500;"">Enable Automatic Scanning</span>
                            </label>
                            <div id=""scan-schedule-config"" style=""display: none;"">
                                <label style=""display: block; margin-bottom: 0.5rem; font-weight: 500;"">Scan Frequency:</label>
                                <select id=""scan-interval"" style=""width: 100%; padding: 0.5rem; border: 1px solid var(--border); border-radius: 4px; background: var(--bg); margin-bottom: 1.5rem;"">
                                    <option value=""1"">Every hour</option>
                                    <option value=""6"">Every 6 hours</option>
                                    <option value=""12"">Every 12 hours</option>
                                    <option value=""24"" selected>Daily (every 24 hours)</option>
                                    <option value=""48"">Every 2 days</option>
                                    <option value=""168"">Weekly (every 7 days)</option>
                                </select>
                                <button class=""btn btn-primary"" onclick=""updateScanSchedule()"" style=""width: 100%; margin-bottom: 1rem;"">Save Schedule</button>
                                <button class=""btn btn-secondary"" onclick=""scheduleNextScanNow()"" style=""width: 100%;"">Schedule Next Scan Now</button>
                            </div>
                        </div>
                    </div>
                    <div class=""action-card"">
                        <h3>Schedule Status</h3>
                        <div style=""margin-top: 1rem; padding: 1.25rem; background: var(--card-bg); border: 1px solid var(--border); border-radius: 4px;"">
                            <div style=""display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1rem;"">
                                <div style=""width: 12px; height: 12px; border-radius: 50%;"" id=""schedule-indicator""></div>
                                <strong style=""font-size: 1rem;"">Status:</strong>
                                <span id=""scan-schedule-status"" style=""font-size: 1rem;"">Loading...</span>
                            </div>
                            <div style=""border-top: 1px solid var(--border); padding-top: 1rem; margin-top: 1rem;"">
                                <p style=""margin: 0 0 0.75rem 0; font-size: 0.9rem; color: var(--text-secondary);"">
                                    <strong>Frequency:</strong> <span id=""scan-frequency"">-</span>
                                </p>
                                <p style=""margin: 0 0 0.75rem 0; font-size: 0.9rem; color: var(--text-secondary);"">
                                    <strong>Last Scan:</strong> <span id=""last-scan-time"">Loading...</span>
                                </p>
                                <p style=""margin: 0; font-size: 1rem;"">
                                    <strong style=""color: var(--accent);"">Next Scan:</strong> 
                                    <span id=""next-scan-time"" style=""font-weight: 500;"">-</span>
                                </p>
                            </div>
                        </div>
                        <button class=""btn btn-success"" onclick=""triggerManualScan()"" style=""margin-top: 1rem; width: 100%;"">
                            🚀 Run Manual Scan Now
                        </button>
                    </div>
                </div>
            </section>
            
            <section style=""margin-top: 2rem; border-top: 2px solid var(--border); padding-top: 2rem;"">
                <h2>🔄 Duplicate Management</h2>
                <p style=""color: var(--text-secondary); margin-bottom: 1rem;"">
                    Analyze and merge duplicate documents (same name, size, and file type from different drives).
                </p>
                <div class=""action-grid"">
                    <div class=""action-card"">
                        <h3>Analyze Duplicates</h3>
                        <p>Scan database for potential duplicate documents.</p>
                        <button class=""btn btn-primary"" onclick=""analyzeDuplicates()"">Analyze</button>
                        <div id=""duplicate-analysis"" style=""margin-top: 1rem;""></div>
                    </div>
                    <div class=""action-card"">
                        <h3>Merge All Duplicates</h3>
                        <p>Automatically merge all duplicate groups found.</p>
                        <button class=""btn btn-warning"" onclick=""mergeAllDuplicates()"">Merge All</button>
                        <div id=""merge-result"" style=""margin-top: 1rem;""></div>
                    </div>
                </div>
                <div id=""duplicate-groups"" style=""margin-top: 1rem;""></div>
            </section>
        </div>
    </main>
    
    <script defer>
        // Tab Switching
        function switchTab(tabName) {{
            // Hide all tabs
            document.querySelectorAll('.tab-content').forEach(tab => {{
                tab.classList.remove('active');
            }});
            
            // Remove active from all buttons
            document.querySelectorAll('.tab-button').forEach(btn => {{
                btn.classList.remove('active');
            }});
            
            // Show selected tab
            const tabElement = document.getElementById(tabName);
            if (tabElement) {{
                tabElement.classList.add('active');
            }}
            
            // Activate button (handle case where event might not be defined)
            if (typeof event !== 'undefined' && event.target) {{
                const button = event.target.closest('.tab-button');
                if (button) {{
                    button.classList.add('active');
                }}
            }}
            
            // Auto-load data for certain tabs
            if (tabName === 'review') {{
                loadReviewQueue();
            }} else if (tabName === 'voting') {{
                loadPendingTags();
            }} else if (tabName === 'drives') {{
                loadDriveList();
            }} else if (tabName === 'system') {{
                loadCacheSettings();
                loadScanSchedule();
            }}
        }}
        
        // Initialize on page load
        window.addEventListener('DOMContentLoaded', () => {{
            updateStatus();
            checkReviewQueue();
            checkVotingQueue();
            // Poll every 30 seconds
            setInterval(() => {{
                updateStatus();
                checkReviewQueue();
                checkVotingQueue();
            }}, 30000);
            
            // Hide tag search results when clicking outside
            document.addEventListener('click', function(e) {{
                const searchInput = document.getElementById('tag-search-input');
                const resultsDiv = document.getElementById('tag-search-results');
                if (searchInput && resultsDiv && !searchInput.contains(e.target) && !resultsDiv.contains(e.target)) {{
                    resultsDiv.style.display = 'none';
                }}
            }});
        }});
        
        // Check review queue and update badge
        async function checkReviewQueue() {{
            try {{
                const response = await fetch('/admin/text-review/queue');
                const data = await response.json();
                const count = data.documents ? data.documents.length : 0;
                
                const badge = document.getElementById('review-badge');
                const summary = document.getElementById('review-queue-summary');
                
                if (count > 0) {{
                    badge.textContent = count;
                    badge.style.display = 'inline-block';
                    if (summary) {{
                        summary.textContent = count + ' document' + (count !== 1 ? 's' : '') + ' need' + (count === 1 ? 's' : '') + ' review';
                        summary.style.color = 'var(--warning)';
                    }}
                }} else {{
                    badge.style.display = 'none';
                    if (summary) {{
                        summary.textContent = 'No documents need review ✓';
                        summary.style.color = 'var(--success)';
                    }}
                }}
            }} catch (e) {{
                console.error('Error checking review queue:', e);
            }}
        }}
        
        // Check voting queue and update badge
        async function checkVotingQueue() {{
            try {{
                const response = await fetch('/api/voting/pending');
                const data = await response.json();
                const suggestions = data.pendingSuggestions || [];
                const removals = data.pendingRemovalRequests || [];
                const count = suggestions.length + removals.length;
                
                const badge = document.getElementById('voting-badge');
                const summary = document.getElementById('voting-summary');
                
                if (count > 0) {{
                    badge.textContent = count;
                    badge.style.display = 'inline-block';
                    if (summary) {{
                        summary.textContent = count + ' pending tag action' + (count !== 1 ? 's' : '');
                        summary.style.color = 'var(--warning)';
                    }}
                }} else {{
                    badge.style.display = 'none';
                    if (summary) {{
                        summary.textContent = 'No pending tag actions ✓';
                        summary.style.color = 'var(--success)';
                    }}
                }}
            }} catch (e) {{
                console.error('Error checking voting queue:', e);
            }}
        }}
        
        async function updateStatus() {{
            // Update batch status
            try {{
                const batchResp = await fetch('/admin/batch/status');
                const batchData = await batchResp.json();
                const batchStatus = document.getElementById('batch-status');
                const batchInfo = document.getElementById('batch-info');
                
                if (batchData.isRunning) {{
                    batchStatus.className = 'status status-running';
                    batchStatus.innerHTML = '<span class=""spinner""></span> Running';
                    batchInfo.innerHTML = 'Currently processing: ' + batchData.currentBatch + '<br>Last run: ' + batchData.lastRun;
                }} else {{
                    batchStatus.className = 'status status-idle';
                    batchStatus.textContent = 'Idle';
                    batchInfo.innerHTML = 'Last run: ' + batchData.lastRun;
                }}
            }} catch (e) {{
                console.error('Failed to update batch status:', e);
            }}
            
            // Update drive scan status
            try {{
                const driveResp = await fetch('/admin/drives/status');
                const driveData = await driveResp.json();
                const driveStatus = document.getElementById('drive-status');
                const driveInfo = document.getElementById('drive-info');
                
                if (driveData.isScanning) {{
                    driveStatus.className = 'status status-running';
                    driveStatus.innerHTML = '<span class=""spinner""></span> Scanning';
                }} else {{
                    driveStatus.className = 'status status-idle';
                    driveStatus.textContent = 'Idle';
                }}
                
                driveInfo.innerHTML = `
                    Last scan: ${{driveData.lastScan}}<br>
                    New documents: ${{driveData.newDocuments}}
                `;
            }} catch (e) {{
                console.error('Failed to update drive status:', e);
            }}
        }}
        
        async function startBatch() {{
            if (!confirm('Start batch processing?')) return;
            try {{
                const resp = await fetch('/admin/batch/start', {{ method: 'POST' }});
                const data = await resp.json();
                
                if (!resp.ok) {{
                    alert('Failed to start batch: ' + (data.error || data.message || 'Unknown error'));
                    return;
                }}
                
                alert(data.message || (data.success ? 'Batch processing started!' : 'Failed to start batch processing'));
                updateStatus();
            }} catch (e) {{
                console.error('Batch start error:', e);
                alert('Error starting batch: ' + e.message);
            }}
        }}
        
        async function stopBatch() {{
            if (!confirm('Stop batch processing?')) return;
            try {{
                const resp = await fetch('/admin/batch/stop', {{ method: 'POST' }});
                const data = await resp.json();
                alert(data.message || 'Batch processing stopped');
                updateStatus();
            }} catch (e) {{
                alert('Error: ' + e.message);
            }}
        }}
        
        async function startDriveScan() {{
            if (!confirm('Start scanning Google Drives? This may take a while.')) return;
            try {{
                const resp = await fetch('/admin/drives/scan', {{ method: 'POST' }});
                const data = await resp.json();
                
                if (!resp.ok) {{
                    alert('Failed to start scan: ' + (data.error || data.message || 'Unknown error'));
                    return;
                }}
                
                alert(data.message || (data.success ? 'Drive scan started!' : 'Failed to start scan'));
                updateStatus();
            }} catch (e) {{
                console.error('Drive scan error:', e);
                alert('Error starting drive scan: ' + e.message);
            }}
        }}
        
        async function stopDriveScan() {{
            if (!confirm('Stop drive scanning?')) return;
            try {{
                const resp = await fetch('/admin/drives/scan/stop', {{ method: 'POST' }});
                const data = await resp.json();
                
                if (!resp.ok) {{
                    alert('Failed to stop scan: ' + (data.error || data.message || 'Unknown error'));
                    return;
                }}
                
                alert(data.message || 'Drive scan stopped');
                updateStatus();
            }} catch (e) {{
                console.error('Stop scan error:', e);
                alert('Error stopping scan: ' + e.message);
            }}
        }}
        
        async function applyGenreTags() {{
            if (!confirm('Apply community genre tags to documents? This may take a few minutes.')) return;
            document.getElementById('genre-info').innerHTML = '<span class=""spinner""></span> Processing...';
            try {{
                const resp = await fetch('/admin/tags/apply-community-genres', {{ method: 'POST' }});
                const data = await resp.json();
                document.getElementById('genre-info').innerHTML = data.message || 
                    '✓ Applied ' + data.newTagsApplied + ' tags to ' + data.matchedDocuments + ' documents';
                alert(data.message || 'Genre tags applied successfully!');
            }} catch (e) {{
                document.getElementById('genre-info').innerHTML = '✗ Error applying tags';
                alert('Error: ' + e.message);
            }}
        }}
        
        async function applySeriesTags() {{
            if (!confirm('Apply community series tags to documents? This may take a few minutes.')) return;
            document.getElementById('series-info').innerHTML = '<span class=""spinner""></span> Processing...';
            try {{
                const resp = await fetch('/admin/tags/apply-community-series', {{ method: 'POST' }});
                const data = await resp.json();
                document.getElementById('series-info').innerHTML = data.message || 
                    '✓ Applied ' + data.newTagsApplied + ' tags to ' + data.matchedDocuments + ' documents';
                alert(data.message || 'Series tags applied successfully!');
            }} catch (e) {{
                document.getElementById('series-info').innerHTML = '✗ Error applying tags';
                alert('Error: ' + e.message);
            }}
        }}
        
        // Tag Recategorization Functions
        let searchDebounceTimer;
        let selectedTag = null;
        
        // Load tag categories dynamically on page load
        async function loadTagCategories() {{
            try {{
                const resp = await fetch('/admin/tags/categories');
                const data = await resp.json();
                
                if (data.success && data.categories) {{
                    const select = document.getElementById('new-category-select');
                    // Keep first option, add categories
                    data.categories.forEach(function(cat) {{
                        const opt = document.createElement('option');
                        opt.value = cat;
                        opt.textContent = cat;
                        select.appendChild(opt);
                    }});
                }}
            }} catch (e) {{
                console.error('Failed to load tag categories:', e);
            }}
        }}
        
        // Initialize when tabs are switched
        if (document.getElementById('new-category-select')) {{
            loadTagCategories();
        }}
        
        function searchTagsDebounced(query) {{
            clearTimeout(searchDebounceTimer);
            searchDebounceTimer = setTimeout(() => searchTags(query), 300);
        }}
        
        async function searchTags(query) {{
            const resultsDiv = document.getElementById('tag-search-results');
            
            if (!query || query.length < 2) {{
                resultsDiv.style.display = 'none';
                return;
            }}
            
            try {{
                const resp = await fetch('/admin/tags/search?query=' + encodeURIComponent(query));
                const data = await resp.json();
                
                if (data.success && data.tags.length > 0) {{
                    resultsDiv.innerHTML = '';
                    data.tags.forEach(function(tag) {{
                        const div = document.createElement('div');
                        div.style.padding = '0.75rem';
                        div.style.cursor = 'pointer';
                        div.style.borderBottom = '1px solid var(--border)';
                        div.style.transition = 'background 0.2s';
                        div.onmouseover = function() {{ this.style.background = 'var(--bg-tertiary)'; }};
                        div.onmouseout = function() {{ this.style.background = 'transparent'; }};
                        div.onclick = function() {{ selectTag(tag.tagName, tag.tagCategory, tag.documentCount); }};
                        
                        const strong = document.createElement('strong');
                        strong.textContent = tag.tagName;
                        div.appendChild(strong);
                        
                        const span = document.createElement('span');
                        span.style.color = 'var(--text-secondary)';
                        span.style.marginLeft = '0.5rem';
                        span.style.fontSize = '0.85rem';
                        span.textContent = '[' + tag.tagCategory + '] · ' + tag.documentCount + ' docs';
                        div.appendChild(span);
                        
                        resultsDiv.appendChild(div);
                    }});
                    resultsDiv.style.display = 'block';
                }} else {{
                    const div = document.createElement('div');
                    div.style.padding = '0.75rem';
                    div.style.color = 'var(--text-secondary)';
                    div.textContent = 'No tags found';
                    resultsDiv.innerHTML = '';
                    resultsDiv.appendChild(div);
                    resultsDiv.style.display = 'block';
                }}
            }} catch (e) {{
                console.error('Error searching tags:', e);
                resultsDiv.style.display = 'none';
            }}
        }}
        
        function selectTag(tagName, tagCategory, documentCount) {{
            selectedTag = {{ tagName, tagCategory, documentCount }};
            
            // Update UI
            document.getElementById('tag-search-input').value = tagName;
            document.getElementById('tag-search-results').style.display = 'none';
            
            document.getElementById('selected-tag-name').textContent = tagName;
            document.getElementById('selected-tag-category').textContent = tagCategory;
            document.getElementById('selected-tag-count').textContent = documentCount;
            
            // Set dropdown to current category and enable it
            const select = document.getElementById('new-category-select');
            select.disabled = false;
            select.value = tagCategory;
            
            // Enable recategorize button
            document.getElementById('recategorize-btn').disabled = false;
            
            document.getElementById('selected-tag-info').style.display = 'block';
            document.getElementById('recategorize-result').innerHTML = '';
        }}
        
        async function recategorizeSelectedTag() {{
            if (!selectedTag) return;
            
            const newCategory = document.getElementById('new-category-select').value;
            if (!newCategory) {{
                alert('Please select a category');
                return;
            }}
            
            if (newCategory === selectedTag.tagCategory) {{
                alert('Please select a different category. Current category is already ""' + newCategory + '""');
                return;
            }}
            
            if (!confirm('Recategorize ""' + selectedTag.tagName + '"" from ""' + selectedTag.tagCategory + '"" to ""' + newCategory + '""?\\n\\nThis will update ' + selectedTag.documentCount + ' documents and any associated approval rules.')) {{
                return;
            }}
            
            const resultDiv = document.getElementById('recategorize-result');
            resultDiv.innerHTML = '<span class=""spinner""></span> Processing...';
            
            try {{
                const resp = await fetch('/admin/tags/recategorize', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{
                        tagName: selectedTag.tagName,
                        oldCategory: selectedTag.tagCategory,
                        newCategory: newCategory
                    }})
                }});
                
                const data = await resp.json();
                
                if (data.success) {{
                    resultDiv.innerHTML = '<span style=""color: var(--success);"">✓ ' + data.message + '</span><br>' +
                        '<span style=""font-size: 0.85rem;"">Updated ' + data.documentTagsUpdated + ' document tags and ' + data.approvedRulesUpdated + ' approval rules</span>';
                    
                    // Reset form
                    selectedTag = null;
                    document.getElementById('tag-search-input').value = '';
                    document.getElementById('selected-tag-info').style.display = 'none';
                    document.getElementById('new-category-select').value = '';
                }} else {{
                    resultDiv.innerHTML = '<span style=""color: var(--accent);"">✗ ' + data.message + '</span>';
                }}
            }} catch (e) {{
                resultDiv.innerHTML = '<span style=""color: var(--accent);"">✗ Error: ' + e.message + '</span>';
            }}
        }}
        
        async function restartServer() {{
            if (!confirm('Restart the server? The page will reload automatically.')) return;
            try {{
                await fetch('/admin/server/restart', {{ method: 'POST' }});
                alert('Server restarting... Page will reload in 10 seconds');
                setTimeout(() => location.reload(), 10000);
            }} catch (e) {{
                alert('Server restart initiated. Reloading page...');
                setTimeout(() => location.reload(), 10000);
            }}
        }}
        
        async function viewLogs() {{
            const logsDiv = document.getElementById('logs');
            logsDiv.style.display = 'block';
            logsDiv.innerHTML = 'Loading logs...';
            try {{
                const resp = await fetch('/admin/batch/logs');
                const data = await resp.json();
                logsDiv.innerHTML = data.logs || 'No logs available';
            }} catch (e) {{
                logsDiv.innerHTML = 'Error loading logs: ' + e.message;
            }}
        }}
        
        // Cache Management Functions
        async function loadCacheSettings() {{
            try {{
                const resp = await fetch('/admin/system/cache-ttl');
                const data = await resp.json();
                document.getElementById('cache-ttl').value = data.minutes;
                document.getElementById('current-cache-ttl').textContent = data.minutes;
            }} catch (e) {{
                console.error('Error loading cache settings:', e);
            }}
        }}
        
        async function updateCacheTTL() {{
            const minutes = parseInt(document.getElementById('cache-ttl').value);
            const statusSpan = document.getElementById('cache-ttl-status');
            
            if (minutes < 1 || minutes > 60) {{
                statusSpan.textContent = 'Invalid value (1-60 minutes)';
                statusSpan.style.color = 'var(--danger)';
                return;
            }}
            
            try {{
                const resp = await fetch('/admin/system/cache-ttl?minutes=' + minutes, {{
                    method: 'POST'
                }});
                
                if (resp.ok) {{
                    statusSpan.textContent = '✓ Cache TTL updated successfully';
                    statusSpan.style.color = 'var(--success)';
                    document.getElementById('current-cache-ttl').textContent = minutes;
                    setTimeout(() => statusSpan.textContent = '', 3000);
                }} else {{
                    statusSpan.textContent = '✗ Failed to update cache TTL';
                    statusSpan.style.color = 'var(--danger)';
                }}
            }} catch (e) {{
                statusSpan.textContent = '✗ Error: ' + e.message;
                statusSpan.style.color = 'var(--danger)';
            }}
        }}
        
        // Scan Scheduling Functions
        async function loadScanSchedule() {{
            try {{
                const resp = await fetch('/admin/system/scan-schedule');
                const data = await resp.json();
                
                // Update UI controls
                document.getElementById('scan-enabled').checked = data.enabled;
                document.getElementById('scan-interval').value = data.intervalHours;
                document.getElementById('scan-schedule-config').style.display = data.enabled ? 'block' : 'none';
                
                // Update status indicator
                const indicator = document.getElementById('schedule-indicator');
                const statusSpan = document.getElementById('scan-schedule-status');
                
                if (data.enabled) {{
                    indicator.style.background = 'var(--success)';
                    indicator.style.boxShadow = '0 0 8px var(--success)';
                    statusSpan.textContent = 'Active';
                    statusSpan.style.color = 'var(--success)';
                }} else {{
                    indicator.style.background = 'var(--text-secondary)';
                    indicator.style.boxShadow = 'none';
                    statusSpan.textContent = 'Disabled';
                    statusSpan.style.color = 'var(--text-secondary)';
                }}
                
                // Update frequency display
                const freqSpan = document.getElementById('scan-frequency');
                if (data.intervalHours === 1) {{
                    freqSpan.textContent = 'Every hour';
                }} else if (data.intervalHours === 6) {{
                    freqSpan.textContent = 'Every 6 hours';
                }} else if (data.intervalHours === 12) {{
                    freqSpan.textContent = 'Every 12 hours';
                }} else if (data.intervalHours === 24) {{
                    freqSpan.textContent = 'Daily';
                }} else if (data.intervalHours === 48) {{
                    freqSpan.textContent = 'Every 2 days';
                }} else if (data.intervalHours === 168) {{
                    freqSpan.textContent = 'Weekly';
                }} else {{
                    freqSpan.textContent = 'Every ' + data.intervalHours + ' hours';
                }}
                
                // Update last scan time
                if (data.lastScanTime) {{
                    const lastScan = new Date(data.lastScanTime);
                    document.getElementById('last-scan-time').textContent = lastScan.toLocaleString();
                }} else {{
                    document.getElementById('last-scan-time').textContent = 'Never';
                }}
                
                // Update next scheduled scan with FIXED time (not recalculated)
                const nextScanSpan = document.getElementById('next-scan-time');
                if (data.enabled && data.nextScheduledScan) {{
                    const nextScan = new Date(data.nextScheduledScan);
                    const now = new Date();
                    const diff = nextScan - now;
                    
                    if (diff > 0) {{
                        const hours = Math.floor(diff / 3600000);
                        const minutes = Math.floor((diff % 3600000) / 60000);
                        nextScanSpan.textContent = nextScan.toLocaleString() + ' (in ' + hours + 'h ' + minutes + 'm)';
                        nextScanSpan.style.color = 'var(--accent)';
                    }} else {{
                        nextScanSpan.textContent = nextScan.toLocaleString() + ' (overdue)';
                        nextScanSpan.style.color = 'var(--warning)';
                    }}
                }} else if (data.enabled) {{
                    nextScanSpan.textContent = 'Not scheduled yet - click ""Schedule Next Scan Now""';
                    nextScanSpan.style.color = 'var(--warning)';
                }} else {{
                    nextScanSpan.textContent = 'Disabled';
                    nextScanSpan.style.color = 'var(--text-secondary)';
                }}
            }} catch (e) {{
                console.error('Error loading scan schedule:', e);
            }}
        }}
        
        function toggleScanScheduling(enabled) {{
            document.getElementById('scan-schedule-config').style.display = enabled ? 'block' : 'none';
            if (!enabled) {{
                updateScanSchedule();
            }}
        }}
        
        async function updateScanSchedule() {{
            const enabled = document.getElementById('scan-enabled').checked;
            const intervalHours = parseInt(document.getElementById('scan-interval').value);
            
            try {{
                const resp = await fetch('/admin/system/scan-schedule', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{ enabled, intervalHours }})
                }});
                
                if (resp.ok) {{
                    alert('Scan schedule updated successfully');
                    await loadScanSchedule();
                }} else {{
                    alert('Failed to update scan schedule');
                }}
            }} catch (e) {{
                alert('Error: ' + e.message);
            }}
        }}
        
        async function scheduleNextScanNow() {{
            if (!confirm('Schedule the next scan to run immediately?')) return;
            
            try {{
                const resp = await fetch('/admin/system/scan-schedule/set-next', {{
                    method: 'POST'
                }});
                
                if (resp.ok) {{
                    alert('Next scan scheduled successfully');
                    await loadScanSchedule();
                }} else {{
                    alert('Failed to schedule next scan');
                }}
            }} catch (e) {{
                alert('Error: ' + e.message);
            }}
        }}
        
        async function triggerManualScan() {{
            if (!confirm('Start manual scan of all configured drives?')) return;
            
            try {{
                const resp = await fetch('/admin/drives/scan', {{
                    method: 'POST'
                }});
                
                if (resp.ok) {{
                    alert('Manual scan started successfully');
                    await loadScanSchedule();
                }} else {{
                    alert('Failed to start manual scan');
                }}
            }} catch (e) {{
                alert('Error: ' + e.message);
            }}
        }}
        
        async function refreshDocumentCount() {{
            const btn = event.target.closest('button');
            const icon = btn.querySelector('i');
            try {{
                btn.disabled = true;
                icon.classList.add('fa-spin');
                const resp = await fetch('/admin/system/refresh-document-count', {{ method: 'POST' }});
                
                if (!resp.ok) {{
                    const errorText = await resp.text();
                    alert('Server error: ' + resp.status + ' - ' + errorText);
                    return;
                }}
                
                const data = await resp.json();
                if (data.success) {{
                    document.getElementById('total-docs').textContent = data.currentCount.toLocaleString();
                    alert('Document count refreshed: ' + data.currentCount.toLocaleString());
                }} else {{
                    alert('Error: ' + (data.error || 'Failed to refresh'));
                }}
            }} catch (error) {{
                alert('Error: ' + error.message);
            }} finally {{
                btn.disabled = false;
                icon.classList.remove('fa-spin');
            }}
        }}
        
        // Duplicate Management Functions
        async function analyzeDuplicates() {{
            const container = document.getElementById('duplicate-analysis');
            const groupsDiv = document.getElementById('duplicate-groups');
            container.innerHTML = '<div style=""color: var(--text-secondary);"">Analyzing duplicates...</div>';
            groupsDiv.innerHTML = '';
            
            try {{
                const response = await fetch('/api/database/analyze-duplicates');
                const data = await response.json();
                
                if (data.success) {{
                    container.innerHTML = `
                        <div style=""background: var(--bg-secondary); padding: 1rem; border-radius: 4px; margin-top: 0.5rem;"">
                            <strong>Analysis Results:</strong><br>
                            📦 Duplicate Groups: ${{data.stats.groupCount}}<br>
                            📄 Total Duplicates: ${{data.stats.totalDuplicateDocuments}}<br>
                            🗑️ Can Remove: ${{data.stats.documentsThatCouldBeRemoved}} documents<br>
                            📊 Largest Group: ${{data.stats.largestDuplicateGroup}} copies
                        </div>
                    `;
                    
                    if (data.duplicateGroups && data.duplicateGroups.length > 0) {{
                        const groupsHtml = `
                            <h3 style=""margin-top: 1rem;"">Top Duplicate Groups (showing first 20):</h3>
                            ${{data.duplicateGroups.map((group, idx) => `
                                <div style=""background: var(--bg-secondary); padding: 1rem; margin: 0.5rem 0; border-radius: 4px; border-left: 3px solid var(--accent);"">
                                    <strong>${{escapeHtml(group.name)}}</strong> 
                                    (${{group.size ? (group.size / 1024).toFixed(2) : '?'}} KB, ${{group.count}} copies)<br>
                                    <div style=""font-size: 0.85rem; color: var(--text-secondary); margin-top: 0.5rem;"">
                                        Sources: ${{group.documents.map(d => d.sourceDrive).join(', ')}}<br>
                                        <button class=""btn btn-sm btn-warning"" onclick=""mergeSpecificGroup(${{idx}})"" style=""margin-top: 0.5rem; padding: 0.3rem 0.8rem; font-size: 0.85rem;"">
                                            Merge This Group
                                        </button>
                                    </div>
                                </div>
                            `).join('')}}
                        `;
                        groupsDiv.innerHTML = groupsHtml;
                    }}
                }} else {{
                    container.innerHTML = `<div style=""color: var(--danger);"">Error: ${{data.error}}</div>`;
                }}
            }} catch (error) {{
                container.innerHTML = `<div style=""color: var(--danger);"">Error: ${{error.message}}</div>`;
            }}
        }}
        
        async function mergeAllDuplicates() {{
            if (!confirm('This will merge ALL duplicate groups. Continue?')) return;
            
            const container = document.getElementById('merge-result');
            container.innerHTML = '<div style=""color: var(--text-secondary);"">Merging duplicates...</div>';
            
            try {{
                const response = await fetch('/api/database/merge-duplicates', {{
                    method: 'POST'
                }});
                const data = await response.json();
                
                if (data.success) {{
                    container.innerHTML = `
                        <div style=""background: var(--success); color: white; padding: 1rem; border-radius: 4px; margin-top: 0.5rem;"">
                            ✅ ${{data.message}}<br>
                            Merged Groups: ${{data.mergedGroups}}<br>
                            Documents Consolidated: ${{data.documentsMerged}}
                        </div>
                    `;
                    // Refresh analysis
                    setTimeout(() => analyzeDuplicates(), 2000);
                }} else {{
                    container.innerHTML = '<div style=""color: var(--danger);"">' + (data.message || 'Merge failed') + '</div>';
                }}
            }} catch (error) {{
                container.innerHTML = `<div style=""color: var(--danger);"">Error: ${{error.message}}</div>`;
            }}
        }}
        
        async function mergeSpecificGroup(groupIndex) {{
            if (!confirm('Merge duplicate group #' + (groupIndex + 1) + '?')) return;
            
            const groupsDiv = document.getElementById('duplicate-groups');
            
            try {{
                const response = await fetch('/api/database/merge-duplicates/' + groupIndex, {{
                    method: 'POST'
                }});
                const data = await response.json();
                
                if (data.success) {{
                    alert('✅ ' + data.message);
                    analyzeDuplicates(); // Refresh
                }} else {{
                    alert('❌ Error: ' + (data.message || 'Merge failed'));
                }}
            }} catch (error) {{
                alert('❌ Error: ' + error.message);
            }}
        }}
        
        // OCR Analytics Functions
        let textLengthChart = null;
        let ocrQualityChart = null;
        
        async function loadOcrAnalytics() {{
            const loadingDiv = document.getElementById('analytics-loading');
            const chartsDiv = document.getElementById('analytics-charts');
            
            loadingDiv.style.display = 'block';
            chartsDiv.style.display = 'none';
            
            try {{
                const response = await fetch('/admin/analytics/ocr-quality');
                const data = await response.json();
                
                if (!data.success) {{
                    alert('Error loading analytics: ' + (data.error || 'Unknown error'));
                    return;
                }}
                
                // Update summary statistics
                document.getElementById('total-docs-stat').textContent = data.totalDocuments.toLocaleString();
                document.getElementById('short-text-stat').textContent = data.textLengthDistribution['0-100'].toLocaleString();
                
                // Calculate OCR document count (all buckets except ""No OCR"")
                const ocrDocCount = Object.entries(data.ocrQualityDistribution)
                    .filter(([key, _]) => key !== 'No OCR')
                    .reduce((sum, [_, count]) => sum + count, 0);
                document.getElementById('ocr-docs-stat').textContent = ocrDocCount.toLocaleString();
                
                // Calculate low quality OCR count (quality < 0.5)
                const lowQualityCount = ['0.0-0.1', '0.1-0.2', '0.2-0.3', '0.3-0.4', '0.4-0.5']
                    .reduce((sum, key) => sum + (data.ocrQualityDistribution[key] || 0), 0);
                document.getElementById('low-quality-stat').textContent = lowQualityCount.toLocaleString();
                
                // Render charts
                renderTextLengthChart(data.textLengthDistribution);
                renderOcrQualityChart(data.ocrQualityDistribution);
                
                loadingDiv.style.display = 'none';
                chartsDiv.style.display = 'block';
            }} catch (error) {{
                alert('Error loading analytics: ' + error.message);
                loadingDiv.style.display = 'none';
            }}
        }}
        
        function renderTextLengthChart(distribution) {{
            const ctx = document.getElementById('text-length-chart');
            
            // Destroy existing chart if it exists
            if (textLengthChart) {{
                textLengthChart.destroy();
            }}
            
            const labels = Object.keys(distribution);
            const values = Object.values(distribution);
            
            textLengthChart = new Chart(ctx, {{
                type: 'bar',
                data: {{
                    labels: labels,
                    datasets: [{{
                        label: 'Document Count',
                        data: values,
                        backgroundColor: 'rgba(233, 69, 96, 0.6)',
                        borderColor: 'rgba(233, 69, 96, 1)',
                        borderWidth: 2
                    }}]
                }},
                options: {{
                    responsive: true,
                    maintainAspectRatio: true,
                    layout: {{
                        padding: {{
                            top: 30
                        }}
                    }},
                    onClick: function(evt, activeEls) {{
                        if (activeEls.length > 0) {{
                            const index = activeEls[0].index;
                            const range = labels[index];
                            searchByTextLength(range);
                        }}
                    }},
                    plugins: {{
                        legend: {{
                            display: false
                        }},
                        tooltip: {{
                            backgroundColor: 'rgba(22, 33, 62, 0.9)',
                            titleColor: '#eee',
                            bodyColor: '#eee',
                            borderColor: '#e94560',
                            borderWidth: 1,
                            callbacks: {{
                                label: function(context) {{
                                    return 'Documents: ' + context.parsed.y.toLocaleString();
                                }}
                            }}
                        }},
                        datalabels: {{
                            anchor: 'end',
                            align: 'top',
                            color: '#eee',
                            font: {{
                                weight: 'bold',
                                size: 11
                            }},
                            formatter: function(value) {{
                                return value.toLocaleString();
                            }}
                        }}
                    }},
                    scales: {{
                        y: {{
                            beginAtZero: true,
                            ticks: {{
                                color: '#aaa'
                            }},
                            grid: {{
                                color: 'rgba(42, 42, 78, 0.5)'
                            }}
                        }},
                        x: {{
                            ticks: {{
                                color: '#aaa',
                                maxRotation: 45,
                                minRotation: 45
                            }},
                            grid: {{
                                color: 'rgba(42, 42, 78, 0.5)'
                            }}
                        }}
                    }}
                }},
                plugins: [ChartDataLabels]
            }});
        }}
        
        function renderOcrQualityChart(distribution) {{
            const ctx = document.getElementById('ocr-quality-chart');
            
            // Destroy existing chart if it exists
            if (ocrQualityChart) {{
                ocrQualityChart.destroy();
            }}
            
            const labels = Object.keys(distribution);
            const values = Object.values(distribution);
            
            // Color code: red for low quality (<0.5), yellow for medium (0.5-0.7), green for high (>0.7)
            const backgroundColors = labels.map(label => {{
                if (label === 'No OCR') return 'rgba(170, 170, 170, 0.6)';
                const midpoint = parseFloat(label.split('-')[0]);
                if (midpoint < 0.5) return 'rgba(231, 76, 60, 0.6)';  // Red
                if (midpoint < 0.7) return 'rgba(243, 156, 18, 0.6)'; // Yellow
                return 'rgba(46, 204, 113, 0.6)';  // Green
            }});
            
            const borderColors = labels.map(label => {{
                if (label === 'No OCR') return 'rgba(170, 170, 170, 1)';
                const midpoint = parseFloat(label.split('-')[0]);
                if (midpoint < 0.5) return 'rgba(231, 76, 60, 1)';
                if (midpoint < 0.7) return 'rgba(243, 156, 18, 1)';
                return 'rgba(46, 204, 113, 1)';
            }});
            
            ocrQualityChart = new Chart(ctx, {{
                type: 'bar',
                data: {{
                    labels: labels,
                    datasets: [{{
                        label: 'Document Count',
                        data: values,
                        backgroundColor: backgroundColors,
                        borderColor: borderColors,
                        borderWidth: 2
                    }}]
                }},
                options: {{
                    responsive: true,
                    maintainAspectRatio: true,
                    layout: {{
                        padding: {{
                            top: 30
                        }}
                    }},
                    onClick: function(evt, activeEls) {{
                        if (activeEls.length > 0) {{
                            const index = activeEls[0].index;
                            const range = labels[index];
                            searchByOcrQuality(range);
                        }}
                    }},
                    plugins: {{
                        legend: {{
                            display: false
                        }},
                        tooltip: {{
                            backgroundColor: 'rgba(22, 33, 62, 0.9)',
                            titleColor: '#eee',
                            bodyColor: '#eee',
                            borderColor: '#e94560',
                            borderWidth: 1,
                            callbacks: {{
                                label: function(context) {{
                                    return 'Documents: ' + context.parsed.y.toLocaleString();
                                }}
                            }}
                        }},
                        datalabels: {{
                            anchor: 'end',
                            align: 'top',
                            color: '#eee',
                            font: {{
                                weight: 'bold',
                                size: 11
                            }},
                            formatter: function(value) {{
                                return value.toLocaleString();
                            }}
                        }}
                    }},
                    scales: {{
                        y: {{
                            beginAtZero: true,
                            ticks: {{
                                color: '#aaa'
                            }},
                            grid: {{
                                color: 'rgba(42, 42, 78, 0.5)'
                            }}
                        }},
                        x: {{
                            ticks: {{
                                color: '#aaa',
                                maxRotation: 45,
                                minRotation: 45
                            }},
                            grid: {{
                                color: 'rgba(42, 42, 78, 0.5)'
                            }}
                        }}
                    }}
                }},
                plugins: [ChartDataLabels]
            }});
        }}
        
        function searchByTextLength(range) {{
            const parts = range.split('-');
            const min = parts[0];
            const max = parts[1] === 'inf' ? '999999' : parts[1];
            const url = '/?minTextLength=' + min + '&maxTextLength=' + max;
            window.open(url, '_blank');
        }}
        
        function searchByOcrQuality(range) {{
            if (range === 'No OCR') {{
                window.open('/?hasOcr=false', '_blank');
                return;
            }}
            const parts = range.split('-');
            const min = parts[0];
            const max = parts[1];
            const url = '/?minOcrQuality=' + min + '&maxOcrQuality=' + max;
            window.open(url, '_blank');
        }}
        
        async function calculateReprocessCount() {{
            const textThreshold = parseInt(document.getElementById('reprocess-text-threshold').value);
            const qualityThreshold = parseFloat(document.getElementById('reprocess-quality-threshold').value);
            const statusSpan = document.getElementById('reprocess-status');
            const countSpan = document.getElementById('reprocess-count');
            const btn = document.getElementById('start-reprocess-btn');
            
            statusSpan.textContent = 'Calculating...';
            countSpan.textContent = '?';
            btn.disabled = true;
            
            try {{
                const response = await fetch('/admin/batch/reprocess-count?textThreshold=' + textThreshold + '&qualityThreshold=' + qualityThreshold);
                const data = await response.json();
                
                if (data.success) {{
                    countSpan.textContent = data.count.toLocaleString();
                    statusSpan.textContent = '';
                    btn.disabled = data.count === 0;
                    if (data.count === 0) {{
                        statusSpan.textContent = 'No documents match these criteria';
                        statusSpan.style.color = 'var(--text-secondary)';
                    }}
                }} else {{
                    statusSpan.textContent = 'Error: ' + (data.error || 'Failed to calculate');
                    statusSpan.style.color = 'var(--error)';
                }}
            }} catch (error) {{
                statusSpan.textContent = 'Error: ' + error.message;
                statusSpan.style.color = 'var(--error)';
            }}
        }}
        
        let reprocessProgressInterval = null;
        
        async function startReprocessing() {{
            const textThreshold = parseInt(document.getElementById('reprocess-text-threshold').value);
            const qualityThreshold = parseFloat(document.getElementById('reprocess-quality-threshold').value);
            const count = document.getElementById('reprocess-count').textContent;
            
            if (!confirm('Start reprocessing ' + count + ' documents?\\n\\nThis will queue them for text extraction with OCR. The process may take a while.')) {{
                return;
            }}
            
            const statusSpan = document.getElementById('reprocess-status');
            const btn = document.getElementById('start-reprocess-btn');
            const calcBtn = document.getElementById('calculate-reprocess-btn');
            
            statusSpan.textContent = 'Starting reprocessing...';
            statusSpan.style.color = 'var(--accent)';
            btn.disabled = true;
            calcBtn.disabled = true;
            
            try {{
                const response = await fetch('/admin/batch/start-reprocess', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{ textThreshold: textThreshold, qualityThreshold: qualityThreshold }})
                }});
                
                const data = await response.json();
                
                if (data.success) {{
                    statusSpan.textContent = 'Queued ' + data.queued + ' documents. Monitoring progress...';
                    statusSpan.style.color = 'var(--accent)';
                    
                    // Start polling for progress
                    if (reprocessProgressInterval) {{
                        clearInterval(reprocessProgressInterval);
                    }}
                    
                    reprocessProgressInterval = setInterval(async () => {{
                        try {{
                            const progressResp = await fetch('/admin/batch/reprocess-progress');
                            const progress = await progressResp.json();
                            
                            const remaining = progress.remaining || 0;
                            const processed = progress.totalQueued - remaining;
                            const total = progress.totalQueued || 0;
                            
                            if (total === 0) {{
                                statusSpan.textContent = '✅ Reprocessing complete! Click Calculate to see updated counts.';
                                statusSpan.style.color = 'var(--success)';
                                clearInterval(reprocessProgressInterval);
                                btn.disabled = false;
                                calcBtn.disabled = false;
                                return;
                            }}
                            
                            const percent = Math.round((processed / total) * 100);
                            statusSpan.textContent = `Processing: ${{processed}}/${{total}} complete (${{percent}}%) - ${{remaining}} remaining`;
                            statusSpan.style.color = 'var(--accent)';
                            
                            // Check if complete
                            if (remaining === 0) {{
                                statusSpan.textContent = '✅ Reprocessing complete! Processed ' + total + ' documents. Click Calculate to see updated counts.';
                                statusSpan.style.color = 'var(--success)';
                                clearInterval(reprocessProgressInterval);
                                btn.disabled = false;
                                calcBtn.disabled = false;
                            }}
                        }} catch (e) {{
                            console.error('Failed to check reprocessing progress:', e);
                        }}
                    }}, 3000); // Poll every 3 seconds
                }} else {{
                    statusSpan.textContent = 'Error: ' + (data.error || 'Failed to start reprocessing');
                    statusSpan.style.color = 'var(--error)';
                    btn.disabled = false;
                    calcBtn.disabled = false;
                }}
            }} catch (error) {{
                statusSpan.textContent = 'Error: ' + error.message;
                statusSpan.style.color = 'var(--error)';
                btn.disabled = false;
                calcBtn.disabled = false;
            }}
        }}
        
        function escapeHtml(text) {{
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }}
        
        // Text Review Queue Functions
        async function loadReviewQueue() {{
            const container = document.getElementById('review-queue');
            container.innerHTML = '<div style=""color: var(--text-secondary);"">Loading review queue...</div>';
            
            try {{
                const response = await fetch('/admin/text-review/queue');
                const data = await response.json();
                
                if (data.success && data.documents && data.documents.length > 0) {{
                    const html = `
                        <table style=""width: 100%; border-collapse: collapse; margin-top: 1rem;"">
                            <thead>
                                <tr style=""border-bottom: 2px solid var(--border); text-align: left;"">
                                    <th style=""padding: 0.5rem;"">Document</th>
                                    <th style=""padding: 0.5rem;"">Flagged By</th>
                                    <th style=""padding: 0.5rem;"">When</th>
                                    <th style=""padding: 0.5rem;"">Text Length</th>
                                    <th style=""padding: 0.5rem;"">Actions</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${{data.documents.map(doc => `
                                    <tr style=""border-bottom: 1px solid var(--border);"">
                                        <td style=""padding: 0.5rem;"">${{escapeHtml(doc.name)}}</td>
                                        <td style=""padding: 0.5rem;"">${{escapeHtml(doc.textReviewFlaggedBy || 'Unknown')}}</td>
                                        <td style=""padding: 0.5rem;"">${{doc.textReviewFlaggedAt ? new Date(doc.textReviewFlaggedAt).toLocaleDateString() : 'N/A'}}</td>
                                        <td style=""padding: 0.5rem;"">${{doc.extractedTextLength ? doc.extractedTextLength.toLocaleString() : '0'}} chars</td>
                                        <td style=""padding: 0.5rem;"">
                                            <a href=""/api/browser/ui?docId=${{doc.id}}"" target=""_blank"" style=""display: inline-block; background: #007bff; color: white; text-decoration: none; padding: 0.4rem 0.8rem; border-radius: 4px; font-size: 0.85rem;"">✏️ Edit Text</a>
                                        </td>
                                    </tr>
                                `).join('')}}
                            </tbody>
                        </table>
                    `;
                    container.innerHTML = html;
                }} else {{
                    container.innerHTML = '<div style=""color: var(--success); padding: 1rem;"">✅ No documents need review!</div>';
                }}
            }} catch (error) {{
                container.innerHTML = '<div style=""color: var(--danger);"">Error loading queue: ' + error.message + '</div>';
            }}
        }}
        
        // Tag Voting Functions
        async function loadPendingTags() {{
            const container = document.getElementById('pending-tags-info');
            container.innerHTML = '<div style=""color: var(--text-secondary);"">Loading pending tags...</div>';
            
            try {{
                const response = await fetch('/api/voting/pending');
                const data = await response.json();
                
                if (!data.success) {{
                    container.innerHTML = `<div style=""color: var(--danger);"">Error: ${{data.message || 'Failed to load'}}</div>`;
                    return;
                }}
                
                const suggestions = data.pendingSuggestions || [];
                const removals = data.pendingRemovalRequests || [];
                
                if (suggestions.length === 0 && removals.length === 0) {{
                    container.innerHTML = '<div style=""color: var(--success); padding: 1rem;"">✅ No pending tag actions!</div>';
                    return;
                }}
                
                let html = '';
                
                if (suggestions.length > 0) {{
                    html += `
                        <h4 style=""margin-top: 1rem; color: var(--accent);"">Tag Suggestions (${{suggestions.length}})</h4>
                        <table style=""width: 100%; border-collapse: collapse; margin-top: 0.5rem;"">
                            <thead>
                                <tr style=""border-bottom: 2px solid var(--border); text-align: left;"">
                                    <th style=""padding: 0.5rem;"">Document</th>
                                    <th style=""padding: 0.5rem;"">Drive Link</th>
                                    <th style=""padding: 0.5rem;"">Tag</th>
                                    <th style=""padding: 0.5rem;"">Category</th>
                                    <th style=""padding: 0.5rem;"">Votes</th>
                                    <th style=""padding: 0.5rem;"">Actions</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${{suggestions.map(s => `
                                    <tr style=""border-bottom: 1px solid var(--border);"">
                                        <td style=""padding: 0.5rem;""><a href=""/?docId=${{s.jumpDocumentId}}"" target=""_blank"" style=""color: var(--accent); text-decoration: none;"">${{escapeHtml(s.documentName || 'Doc #' + s.jumpDocumentId)}}</a></td>
                                        <td style=""padding: 0.5rem;""><a href=""${{s.googleDriveLink || '#'}}"" target=""_blank"" style=""color: var(--accent); text-decoration: none;"" title=""Open in Google Drive"">🔗 Drive</a></td>
                                        <td style=""padding: 0.5rem;""><strong>${{escapeHtml(s.tagName)}}</strong></td>
                                        <td style=""padding: 0.5rem;"">
                                            <select id=""category-${{s.id}}"" style=""padding: 0.2rem; border: 1px solid var(--border); border-radius: 4px; background: var(--bg);"">
                                                <option value=""Drive"" ${{s.tagCategory === 'Drive' ? 'selected' : ''}}>Drive</option>
                                                <option value=""Genre"" ${{s.tagCategory === 'Genre' ? 'selected' : ''}}>Genre</option>
                                                <option value=""Series"" ${{s.tagCategory === 'Series' ? 'selected' : ''}}>Series</option>
                                                <option value=""Content"" ${{s.tagCategory === 'Content' ? 'selected' : ''}}>Content</option>
                                                <option value=""ContentType"" ${{s.tagCategory === 'ContentType' ? 'selected' : ''}}>Content Type</option>
                                                <option value=""Quality"" ${{s.tagCategory === 'Quality' ? 'selected' : ''}}>Quality</option>
                                                <option value=""Format"" ${{s.tagCategory === 'Format' ? 'selected' : ''}}>Format</option>
                                                <option value=""Size"" ${{s.tagCategory === 'Size' ? 'selected' : ''}}>Size</option>
                                                <option value=""Version"" ${{s.tagCategory === 'Version' ? 'selected' : ''}}>Version</option>
                                                <option value=""Extraction"" ${{s.tagCategory === 'Extraction' ? 'selected' : ''}}>Extraction</option>
                                                <option value=""Other"" ${{s.tagCategory === 'Other' ? 'selected' : ''}}>Other</option>
                                            </select>
                                        </td>
                                        <td style=""padding: 0.5rem;"">👍 ${{Math.round(s.favorVotes)}} / 👎 ${{Math.round(s.againstVotes)}}</td>
                                        <td style=""padding: 0.5rem;"">
                                            <button onclick=""approveSuggestion(${{s.id}})"" style=""background: var(--success); color: white; border: none; padding: 0.3rem 0.6rem; border-radius: 4px; cursor: pointer; margin-right: 0.5rem;"">✓ Approve</button>
                                            <button onclick=""rejectSuggestion(${{s.id}})"" style=""background: var(--danger); color: white; border: none; padding: 0.3rem 0.6rem; border-radius: 4px; cursor: pointer;"">✗ Reject</button>
                                        </td>
                                    </tr>
                                `).join('')}}
                            </tbody>
                        </table>
                    `;
                }}
                
                if (removals.length > 0) {{
                    html += `
                        <h4 style=""margin-top: 1.5rem; color: var(--accent);"">Tag Removal Requests (${{removals.length}})</h4>
                        <table style=""width: 100%; border-collapse: collapse; margin-top: 0.5rem;"">
                            <thead>
                                <tr style=""border-bottom: 2px solid var(--border); text-align: left;"">
                                    <th style=""padding: 0.5rem;"">Document</th>
                                    <th style=""padding: 0.5rem;"">Drive Link</th>
                                    <th style=""padding: 0.5rem;"">Tag</th>
                                    <th style=""padding: 0.5rem;"">Category</th>
                                    <th style=""padding: 0.5rem;"">Votes</th>
                                    <th style=""padding: 0.5rem;"">Actions</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${{removals.map(r => `
                                    <tr style=""border-bottom: 1px solid var(--border);"">
                                        <td style=""padding: 0.5rem;""><a href=""/?docId=${{r.jumpDocumentId}}"" target=""_blank"" style=""color: var(--accent); text-decoration: none;"">${{escapeHtml(r.documentName || 'Doc #' + r.jumpDocumentId)}}</a></td>
                                        <td style=""padding: 0.5rem;""><a href=""${{r.googleDriveLink || '#'}}"" target=""_blank"" style=""color: var(--accent); text-decoration: none;"" title=""Open in Google Drive"">🔗 Drive</a></td>
                                        <td style=""padding: 0.5rem;""><strong>${{escapeHtml(r.tagName)}}</strong></td>
                                        <td style=""padding: 0.5rem;"">${{escapeHtml(r.tagCategory || 'N/A')}}</td>
                                        <td style=""padding: 0.5rem;"">👍 ${{Math.round(r.favorVotes)}} / 👎 ${{Math.round(r.againstVotes)}}</td>
                                        <td style=""padding: 0.5rem;"">
                                            <button onclick=""approveRemoval(${{r.id}})"" style=""background: var(--success); color: white; border: none; padding: 0.3rem 0.6rem; border-radius: 4px; cursor: pointer; margin-right: 0.5rem;"">✓ Approve Removal</button>
                                            <button onclick=""rejectRemoval(${{r.id}})"" style=""background: var(--danger); color: white; border: none; padding: 0.3rem 0.6rem; border-radius: 4px; cursor: pointer;"">✗ Keep Tag</button>
                                        </td>
                                    </tr>
                                `).join('')}}
                            </tbody>
                        </table>
                    `;
                }}
                
                container.innerHTML = html;
            }} catch (error) {{
                container.innerHTML = `<div style=""color: var(--danger);"">Error: ${{error.message}}</div>`;
            }}
        }}
        
        async function approveSuggestion(id) {{
            try {{
                // Get the selected category from the dropdown
                const categoryDropdown = document.getElementById('category-' + id);
                const selectedCategory = categoryDropdown ? categoryDropdown.value : null;
                
                const url = selectedCategory 
                    ? '/api/voting/admin/approve-suggestion/' + id + '?categoryOverride=' + encodeURIComponent(selectedCategory)
                    : '/api/voting/admin/approve-suggestion/' + id;
                    
                const response = await fetch(url, {{ 
                    method: 'POST',
                    credentials: 'same-origin'
                }});
                const data = await response.json();
                if (data.success) {{
                    alert('✓ Tag suggestion approved!');
                    loadPendingTags(); // Reload the list
                }} else {{
                    alert('Error: ' + (data.message || 'Failed to approve'));
                }}
            }} catch (error) {{
                alert('Error: ' + error.message);
            }}
        }}
        
        async function rejectSuggestion(id) {{
            try {{
                const response = await fetch('/api/voting/admin/reject-suggestion/' + id, {{ 
                    method: 'POST',
                    credentials: 'same-origin'
                }});
                if (!response.ok) {{
                    const text = await response.text();
                    alert('Error: ' + (text || 'Failed to reject suggestion'));
                    return;
                }}
                const data = await response.json();
                if (data.success) {{
                    alert('✓ Tag suggestion rejected');
                    loadPendingTags(); // Reload the list
                }} else {{
                    alert('Error: ' + (data.message || 'Failed to reject'));
                }}
            }} catch (error) {{
                alert('Error: ' + error.message);
            }}
        }}
        
        async function approveRemoval(id) {{
            try {{
                const response = await fetch('/api/voting/admin/approve-removal/' + id, {{ 
                    method: 'POST',
                    credentials: 'same-origin'
                }});
                const data = await response.json();
                if (data.success) {{
                    alert('✓ Tag removal approved');
                    loadPendingTags(); // Reload the list
                }} else {{
                    alert('Error: ' + (data.message || 'Failed to approve'));
                }}
            }} catch (error) {{
                alert('Error: ' + error.message);
            }}
        }}
        
        async function rejectRemoval(id) {{
            try {{
                const response = await fetch('/api/voting/admin/reject-removal/' + id, {{ 
                    method: 'POST',
                    credentials: 'same-origin'
                }});
                if (!response.ok) {{
                    const text = await response.text();
                    alert('Error: ' + (text || 'Failed to reject removal'));
                    return;
                }}
                const data = await response.json();
                if (data.success) {{
                    alert('✓ Tag kept (removal rejected)');
                    loadPendingTags(); // Reload the list
                }} else {{
                    alert('Error: ' + (data.message || 'Failed to reject'));
                }}
            }} catch (error) {{
                alert('Error: ' + error.message);
            }}
        }}
        
        async function showVotingConfig() {{
            const modal = document.createElement('div');
            modal.style.cssText = 'position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.7); display: flex; align-items: center; justify-content: center; z-index: 10000;';
            
            const content = document.createElement('div');
            content.style.cssText = 'background: var(--bg-primary); padding: 2rem; border-radius: 8px; max-width: 500px; width: 90%;';
            content.innerHTML = '<div style=""color: var(--text-secondary);"">Loading configuration...</div>';
            
            modal.appendChild(content);
            document.body.appendChild(modal);
            
            try {{
                const response = await fetch('/api/voting/config');
                const data = await response.json();
                
                if (!data.success) {{
                    content.innerHTML = `<div style=""color: var(--danger);"">Error loading config</div>`;
                    return;
                }}
                
                const config = data.config || {{}};
                
                content.innerHTML = `
                    <h2 style=""margin-top: 0; color: var(--accent);"">Voting Configuration</h2>
                    <form id=""voting-config-form"" style=""margin: 1rem 0;"">
                        <div style=""margin-bottom: 1rem;"">
                            <label style=""display: block; margin-bottom: 0.5rem;"">Auto-Approve Threshold (upvotes):</label>
                            <input type=""number"" id=""approval-threshold"" value=""${{config.autoApproveThreshold || 5}}"" min=""1"" style=""width: 100%; padding: 0.5rem; border: 1px solid var(--border); border-radius: 4px; background: var(--bg-secondary); color: var(--text-primary);"">
                        </div>
                        <div style=""margin-bottom: 1rem;"">
                            <label style=""display: block; margin-bottom: 0.5rem;"">Auto-Reject Threshold (downvotes):</label>
                            <input type=""number"" id=""rejection-threshold"" value=""${{config.autoRejectThreshold || 5}}"" min=""1"" style=""width: 100%; padding: 0.5rem; border: 1px solid var(--border); border-radius: 4px; background: var(--bg-secondary); color: var(--text-primary);"">
                        </div>
                        <div style=""margin-bottom: 1rem;"">
                            <label style=""display: flex; align-items: center;"">
                                <input type=""checkbox"" id=""enable-auto-processing"" ${{config.enableAutoProcessing ? 'checked' : ''}} style=""margin-right: 0.5rem;"">
                                Enable automatic processing
                            </label>
                        </div>
                        <div style=""display: flex; gap: 0.5rem; margin-top: 1.5rem;"">
                            <button type=""button"" onclick=""saveVotingConfig()"" style=""flex: 1; background: var(--success); color: white; border: none; padding: 0.75rem; border-radius: 4px; cursor: pointer; font-weight: bold;"">💾 Save</button>
                            <button type=""button"" onclick=""closeVotingConfig()"" style=""flex: 1; background: var(--danger); color: white; border: none; padding: 0.75rem; border-radius: 4px; cursor: pointer;"">✗ Cancel</button>
                        </div>
                    </form>
                `;
            }} catch (error) {{
                content.innerHTML = `<div style=""color: var(--danger);"">Error: ${{error.message}}</div>`;
            }}
        }}
        
        async function saveVotingConfig() {{
            try {{
                const config = {{
                    autoApproveThreshold: parseInt(document.getElementById('approval-threshold').value),
                    autoRejectThreshold: parseInt(document.getElementById('rejection-threshold').value),
                    enableAutoProcessing: document.getElementById('enable-auto-processing').checked
                }};
                
                const response = await fetch('/api/voting/config', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify(config)
                }});
                
                const data = await response.json();
                if (data.success) {{
                    alert('✓ Configuration saved!');
                    closeVotingConfig();
                }} else {{
                    alert('Error: ' + (data.message || 'Failed to save'));
                }}
            }} catch (error) {{
                alert('Error: ' + error.message);
            }}
        }}
        
        function closeVotingConfig() {{
            const modal = document.querySelector('div[style*=""position: fixed""]');
            if (modal) modal.remove();
        }}
        
        // Tag Hierarchy Functions
        async function loadTagHierarchies() {{
            const container = document.getElementById('hierarchy-list');
            container.innerHTML = '<div style=""color: var(--text-secondary);"">Loading tag relationships...</div>';
            
            try {{
                const response = await fetch('/api/tags/hierarchy/all');
                const data = await response.json();
                
                if (!data.success) {{
                    container.innerHTML = `<div style=""color: var(--danger);"">Error: ${{data.message || 'Failed to load'}}</div>`;
                    return;
                }}
                
                const hierarchies = data.hierarchies || [];
                
                if (hierarchies.length === 0) {{
                    container.innerHTML = '<div style=""color: var(--text-secondary); padding: 1rem; text-align: center;"">No tag relationships defined yet. Use the form above to create parent-child relationships.</div>';
                    return;
                }}
                
                // Group hierarchies by parent tag
                const grouped = {{}};
                hierarchies.forEach(h => {{
                    if (!grouped[h.parentTagName]) {{
                        grouped[h.parentTagName] = [];
                    }}
                    grouped[h.parentTagName].push(h);
                }});
                
                let html = '<div style=""display: flex; flex-direction: column; gap: 1rem;"">';
                
                Object.keys(grouped).sort().forEach(parent => {{
                    const children = grouped[parent].sort((a, b) => a.childTagName.localeCompare(b.childTagName));
                    
                    html += `
                        <div style=""background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: 8px; padding: 1rem;"">
                            <div style=""display: flex; align-items: center; margin-bottom: 0.75rem;"">
                                <span style=""background: var(--accent); color: white; padding: 0.35rem 0.7rem; border-radius: 4px; font-weight: 500; font-size: 0.95rem;"">${{escapeHtml(parent)}}</span>
                                <i class=""fas fa-arrow-right"" style=""margin: 0 1rem; color: var(--text-secondary);""></i>
                                <span style=""color: var(--text-secondary); font-size: 0.9rem;"">has ${{children.length}} child tag${{children.length !== 1 ? 's' : ''}}</span>
                            </div>
                            <div style=""display: flex; flex-wrap: wrap; gap: 0.5rem; padding-left: 2rem;"">
                                ${{children.map(c => `
                                    <div style=""display: flex; align-items: center; gap: 0.5rem; background: var(--bg-secondary); border: 1px solid var(--border); border-radius: 4px; padding: 0.35rem 0.5rem;"">
                                        <span style=""font-size: 0.9rem;"">${{escapeHtml(c.childTagName)}}</span>
                                        <button onclick=""removeTagHierarchy(${{c.id}}, '${{escapeHtml(parent)}}', '${{escapeHtml(c.childTagName)}}')"" 
                                                style=""background: none; border: none; color: var(--danger); cursor: pointer; padding: 0.1rem 0.3rem; font-size: 0.85rem;"" 
                                                title=""Remove this relationship"">
                                            <i class=""fas fa-trash""></i>
                                        </button>
                                    </div>
                                `).join('')}}
                            </div>
                        </div>
                    `;
                }});
                
                html += '</div>';
                container.innerHTML = html;
            }} catch (error) {{
                container.innerHTML = `<div style=""color: var(--danger);"">Error: ${{error.message}}</div>`;
            }}
        }}
        
        async function addTagHierarchy() {{
            const parentInput = document.getElementById('parent-tag');
            const childInput = document.getElementById('child-tag');
            
            const parentTag = parentInput.value.trim();
            const childTag = childInput.value.trim();
            
            if (!parentTag || !childTag) {{
                alert('Please enter both parent and child tag names');
                return;
            }}
            
            if (parentTag === childTag) {{
                alert('Parent and child tags cannot be the same');
                return;
            }}
            
            try {{
                const response = await fetch('/api/tags/hierarchy', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{
                        parentTagName: parentTag,
                        childTagName: childTag
                    }})
                }});
                
                const data = await response.json();
                
                if (data.success) {{
                    alert('✓ Tag relationship created: ' + parentTag + ' → ' + childTag);
                    parentInput.value = '';
                    childInput.value = '';
                    await loadTagHierarchies();
                }} else {{
                    alert('Error: ' + (data.message || 'Failed to create relationship'));
                }}
            }} catch (error) {{
                alert('Error: ' + error.message);
            }}
        }}
        
        async function removeTagHierarchy(hierarchyId, parentTag, childTag) {{
            if (!confirm('Remove relationship: ' + parentTag + ' → ' + childTag + '?')) {{
                return;
            }}
            
            try {{
                const response = await fetch('/api/tags/hierarchy/' + hierarchyId, {{
                    method: 'DELETE'
                }});
                
                const data = await response.json();
                
                if (data.success) {{
                    alert('✓ Tag relationship removed');
                    await loadTagHierarchies();
                }} else {{
                    alert('Error: ' + (data.message || 'Failed to remove relationship'));
                }}
            }} catch (error) {{
                alert('Error: ' + error.message);
            }}
        }}
        
        // Drive Management Functions
        async function loadDriveList() {{
            const container = document.getElementById('drive-list');
            container.innerHTML = '<div style=""color: var(--text-secondary);"">Loading drives...</div>';
            
            try {{
                const response = await fetch('/admin/drive-configurations');
                const data = await response.json();
                
                if (!data.success || !data.drives || data.drives.length === 0) {{
                    container.innerHTML = '<div style=""color: var(--text-secondary);"">No drives configured.</div>';
                    return;
                }}
                
                let html = '<div style=""display: flex; flex-direction: column; gap: 1rem;"">';
                
                data.drives.forEach((drive, index) => {{
                    const lastScan = drive.lastScanTime ? new Date(drive.lastScanTime).toLocaleString() : 'Never';
                    const statusColor = drive.isActive ? 'var(--success)' : 'var(--danger)';
                    
                    html += `
                        <div style=""background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: 8px; padding: 1rem;"">
                            <div style=""display: flex; justify-content: space-between; align-items: start; cursor: pointer;"" onclick=""toggleDriveFolders(${{index}}, '${{escapeHtml(drive.driveName)}}')"">
                                <div style=""flex: 1;"">
                                    <h3 style=""margin: 0 0 0.5rem 0; color: var(--text-primary); font-size: 1.1rem;"">
                                        <span id=""drive-arrow-${{index}}"" style=""display: inline-block; width: 20px; transition: transform 0.3s;"">▶</span>
                                        ${{escapeHtml(drive.driveName)}}
                                    </h3>
                                    <div style=""display: flex; gap: 2rem; color: var(--text-secondary); font-size: 0.85rem;"">
                                        <span>📊 ${{drive.documentCount}} documents</span>
                                        <span>📁 ${{drive.folderCount}} folders</span>
                                        <span>⏱️ Last scan: ${{lastScan}}</span>
                                        <span style=""color: ${{statusColor}};"">● ${{drive.isActive ? 'Active' : 'Inactive'}}</span>
                                    </div>
                                </div>
                                <div style=""display: flex; gap: 0.5rem;"" onclick=""event.stopPropagation();"">
                                    <button class=""btn btn-success"" style=""padding: 0.4rem 0.8rem; font-size: 0.85rem;"" onclick=""scanDrive('${{escapeHtml(drive.driveName)}}', ${{index}})"">\ud83d\udd04 Scan</button>
                                    <button class=""btn btn-primary"" style=""padding: 0.4rem 0.8rem; font-size: 0.85rem;"" onclick=""refreshFolders('${{escapeHtml(drive.driveName)}}', ${{index}})"">\ud83d\udcc2 Refresh Folders</button>
                                </div>
                            </div>
                            <div id=""drive-folders-${{index}}"" style=""display: none; margin-top: 1rem; padding-top: 1rem; border-top: 1px solid var(--border);"">
                                <p style=""color: var(--text-secondary); font-size: 0.85rem;"">Click to load folders...</p>
                            </div>
                            <div id=""drive-status-${{index}}"" style=""margin-top: 0.5rem; display: none; padding: 0.5rem; background: var(--bg-secondary); border-radius: 4px; font-size: 0.85rem;""></div>
                        </div>
                    `;
                }});
                
                html += '</div>';
                container.innerHTML = html;
            }} catch (error) {{
                container.innerHTML = '<div style=""color: var(--danger);"">Error loading drives: ' + error.message + '</div>';
            }}
        }}
        
        async function toggleDriveFolders(index, driveName) {{
            const arrow = document.getElementById(`drive-arrow-${{index}}`);
            const container = document.getElementById(`drive-folders-${{index}}`);
            
            if (container.style.display === 'none') {{
                arrow.style.transform = 'rotate(90deg)';
                container.style.display = 'block';
                await loadDriveFolders(index, driveName);
            }} else {{
                arrow.style.transform = 'rotate(0deg)';
                container.style.display = 'none';
            }}
        }}
        
        async function loadDriveFolders(index, driveName) {{
            const container = document.getElementById(`drive-folders-${{index}}`);
            container.innerHTML = '<div style=""color: var(--text-secondary); font-size: 0.85rem;""><span class=""spinner""></span> Loading folders...</div>';
            
            try {{
                const response = await fetch('/admin/drives/' + encodeURIComponent(driveName) + '/folders');
                const folders = await response.json();
                
                if (!folders || folders.length === 0) {{
                    container.innerHTML = '<div style=""color: var(--text-secondary); font-size: 0.85rem;"">No folders found (flat structure)</div>';
                    return;
                }}
                
                let html = '<div style=""display: flex; flex-direction: column; gap: 0.25rem;"">';
                folders.forEach(folder => {{
                    html += `
                        <div style=""padding: 0.4rem 0.8rem; background: var(--bg-secondary); border-radius: 4px; font-size: 0.85rem; display: flex; justify-content: space-between; align-items: center;"">
                            <span>📁 ${{escapeHtml(folder.folderName)}}</span>
                            <span style=""color: var(--text-muted); font-size: 0.75rem;"">ID: ${{folder.folderId}}</span>
                        </div>
                    `;
                }});
                html += '</div>';
                
                container.innerHTML = html;
            }} catch (error) {{
                container.innerHTML = '<div style=""color: var(--danger); font-size: 0.85rem;"">Error: ' + error.message + '</div>';
            }}
        }}
        
        async function scanDrive(driveName, index) {{
            const statusDiv = document.getElementById('drive-status-' + index);
            statusDiv.style.display = 'block';
            statusDiv.style.color = 'var(--text-secondary)';
            statusDiv.innerHTML = '<span class=""spinner""></span> Scanning drive...';
            
            try {{
                const response = await fetch('/admin/drives/' + encodeURIComponent(driveName) + '/scan', {{
                    method: 'POST'
                }});
                const data = await response.json();
                
                if (data.success) {{
                    statusDiv.style.color = 'var(--success)';
                    statusDiv.innerHTML = `✓ Scan complete! Found ${{data.newDocuments}} new documents`;
                    setTimeout(() => {{
                        statusDiv.style.display = 'none';
                        loadDriveList();
                    }}, 3000);
                }} else {{
                    statusDiv.style.color = 'var(--danger)';
                    statusDiv.innerHTML = `✗ Error: ${{data.error}}`;
                }}
            }} catch (error) {{
                statusDiv.style.color = 'var(--danger)';
                statusDiv.innerHTML = `✗ Error: ${{error.message}}`;
            }}
        }}
        
        async function refreshFolders(driveName, index) {{
            const statusDiv = document.getElementById('drive-status-' + index);
            statusDiv.style.display = 'block';
            statusDiv.style.color = 'var(--text-secondary)';
            statusDiv.innerHTML = '<span class=""spinner""></span> Discovering folders...';
            
            try {{
                const response = await fetch('/admin/drives/' + encodeURIComponent(driveName) + '/refresh-folders', {{
                    method: 'POST'
                }});
                const data = await response.json();
                
                if (data.success) {{
                    statusDiv.style.color = 'var(--success)';
                    statusDiv.innerHTML = `✓ ${{data.message}} (${{data.foldersCreated}} new, ${{data.foldersUpdated}} updated)`;
                    
                    const container = document.getElementById(`drive-folders-${{index}}`);
                    if (container.style.display === 'block') {{
                        await loadDriveFolders(index, driveName);
                    }}
                    
                    setTimeout(() => {{
                        statusDiv.style.display = 'none';
                        loadDriveList();
                    }}, 3000);
                }} else {{
                    statusDiv.style.color = 'var(--danger)';
                    statusDiv.innerHTML = `✗ Error: ${{data.error}}`;
                }}
            }} catch (error) {{
                statusDiv.style.color = 'var(--danger)';
                statusDiv.innerHTML = `✗ Error: ${{error.message}}`;
            }}
        }}
        
        // Account Management Functions
        async function changeUsername(event) {{
            event.preventDefault();
            
            const newUsername = document.getElementById('new-username').value;
            const currentPassword = document.getElementById('username-current-password').value;
            const resultDiv = document.getElementById('username-result');
            
            if (!newUsername || !currentPassword) {{
                resultDiv.innerHTML = '<span style=""color: var(--danger);"">Please fill in all fields</span>';
                return;
            }}
            
            resultDiv.innerHTML = '<span style=""color: var(--text-secondary);"">Updating username...</span>';
            
            try {{
                const response = await fetch('/admin/account/change-username', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{
                        newUsername: newUsername,
                        currentPassword: currentPassword
                    }})
                }});
                
                const data = await response.json();
                
                if (data.success) {{
                    resultDiv.innerHTML = '<span style=""color: var(--success);"">✓ Username updated successfully!</span>';
                    document.getElementById('new-username').value = '';
                    document.getElementById('username-current-password').value = '';
                }} else {{
                    resultDiv.innerHTML = '<span style=""color: var(--danger);"">✗ ' + (data.message || 'Failed to update username') + '</span>';
                }}
            }} catch (error) {{
                resultDiv.innerHTML = `<span style=""color: var(--danger);"">✗ Error: ${{error.message}}</span>`;
            }}
        }}
        
        async function changePassword(event) {{
            event.preventDefault();
            
            const currentPassword = document.getElementById('current-password').value;
            const newPassword = document.getElementById('new-password').value;
            const confirmPassword = document.getElementById('confirm-password').value;
            const resultDiv = document.getElementById('password-result');
            
            if (!currentPassword || !newPassword || !confirmPassword) {{
                resultDiv.innerHTML = '<span style=""color: var(--danger);"">Please fill in all fields</span>';
                return;
            }}
            
            if (newPassword !== confirmPassword) {{
                resultDiv.innerHTML = '<span style=""color: var(--danger);"">New passwords do not match</span>';
                return;
            }}
            
            if (newPassword.length < 8) {{
                resultDiv.innerHTML = '<span style=""color: var(--danger);"">Password must be at least 8 characters</span>';
                return;
            }}
            
            resultDiv.innerHTML = '<span style=""color: var(--text-secondary);"">Updating password...</span>';
            
            try {{
                const response = await fetch('/admin/account/change-password', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{
                        currentPassword: currentPassword,
                        newPassword: newPassword
                    }})
                }});
                
                const data = await response.json();
                
                if (data.success) {{
                    resultDiv.innerHTML = '<span style=""color: var(--success);"">✓ Password updated successfully! You will be logged out...</span>';
                    document.getElementById('current-password').value = '';
                    document.getElementById('new-password').value = '';
                    document.getElementById('confirm-password').value = '';
                    // Redirect to login after 2 seconds
                    setTimeout(() => {{ window.location.href = '/admin/login'; }}, 2000);
                }} else {{
                    resultDiv.innerHTML = '<span style=""color: var(--danger);"">✗ ' + (data.message || 'Failed to update password') + '</span>';
                }}
            }} catch (error) {{
                resultDiv.innerHTML = `<span style=""color: var(--danger);"">✗ Error: ${{error.message}}</span>`;
            }}
        }}
        
        function escapeHtml(text) {{
            if (!text) return '';
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }}
    </script>
</body>
</html>";

        await context.Response.WriteAsync(html);
        return Results.Empty;
    }

    private static async Task<IResult> GetSystemStatus(HttpContext context, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var totalDocuments = await dbContext.JumpDocuments.CountAsync();
            var processedDocuments = await dbContext.JumpDocuments
                .CountAsync(d => !string.IsNullOrEmpty(d.ExtractedText));

            return Results.Ok(new {
                success = true,
                serverRunning = true,
                totalDocuments,
                processedDocuments,
                timestamp = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> GetBatchStatus(HttpContext context, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        // Check if batch processing is running (simple file-based approach)
        var batchPidFile = "batch-process.pid";
        var isRunning = false;
        var currentBatch = "";
        
        if (File.Exists(batchPidFile))
        {
            try
            {
                var pidContent = File.ReadAllText(batchPidFile);
                if (int.TryParse(pidContent, out int pid))
                {
                    var process = Process.GetProcessById(pid);
                    isRunning = !process.HasExited;
                    currentBatch = $"PID: {pid}";
                }
            }
            catch
            {
                isRunning = false;
                File.Delete(batchPidFile); // Clean up stale PID file
            }
        }

        var lastRunFile = "batch-last-run.txt";
        var lastRun = File.Exists(lastRunFile) ? File.ReadAllText(lastRunFile) : "Never";

        return Results.Ok(new
        {
            success = true,
            isRunning,
            currentBatch,
            lastRun
        });
    }

    private static async Task<IResult> StartBatchProcessing(HttpContext context, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            // Call the batch processing endpoint directly
            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsync("http://localhost:5248/api/batch/start?batchSize=10", null);
            var content = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                return Results.Ok(new { 
                    success = true, 
                    message = "Batch processing started successfully",
                    details = content
                });
            }
            else
            {
                return Results.BadRequest(new { 
                    success = false, 
                    error = "Failed to start batch processing",
                    details = content
                });
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> StopBatchProcessing(HttpContext context, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            // Call the batch processing stop API endpoint
            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsync("http://localhost:5248/api/batch/stop", null);
            var content = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                return Results.Ok(new { 
                    success = true, 
                    message = "Batch processing stopped successfully"
                });
            }
            else
            {
                return Results.BadRequest(new { 
                    success = false, 
                    error = "Failed to stop batch processing"
                });
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> RestartServer(HttpContext context, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            bool isWindows = OperatingSystem.IsWindows();
            string scriptPath;
            ProcessStartInfo startInfo;

            if (isWindows)
            {
                // Windows: PowerShell script
                var restartScript = @"
Start-Sleep -Seconds 2
Get-Process -Name 'dotnet' -ErrorAction SilentlyContinue | Where-Object { $_.MainModule.FileName -like '*JumpChainSearch*' } | Stop-Process -Force
Start-Sleep -Seconds 3
Set-Location '" + Directory.GetCurrentDirectory() + @"'
Start-Process -FilePath 'dotnet' -ArgumentList 'run --urls http://0.0.0.0:5248' -WindowStyle Hidden
";
                
                scriptPath = "restart-server.ps1";
                File.WriteAllText(scriptPath, restartScript);
                
                startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-ExecutionPolicy Bypass -File restart-server.ps1",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                // Linux: Bash script with systemctl
                var restartScript = @"#!/bin/bash
sleep 2
sudo systemctl restart jumpchain
";
                
                scriptPath = "restart-server.sh";
                File.WriteAllText(scriptPath, restartScript);
                
                // Make script executable
                Process.Start("chmod", $"+x {scriptPath}")?.WaitForExit();
                
                startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = scriptPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            
            Process.Start(startInfo);
            
            return Results.Ok(new { success = true, message = "Server restart initiated" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> GetBatchLogs(HttpContext context, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            // Check for both old batch-processing and new batch-extraction logs
            var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            
            // Ensure logs directory exists
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }
            
            var logFiles = Directory.GetFiles(logsDir, "batch-extraction-*.log")
                .Concat(Directory.GetFiles(logsDir, "batch-processing-*.log"))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(1);

            if (logFiles.Any())
            {
                var latestLog = logFiles.First();
                var logContent = File.ReadAllLines(latestLog).TakeLast(50).ToList();
                // Ensure each line starts fresh with HTML line breaks
                var formattedLogs = string.Join("<br/>", logContent.Select(line => System.Web.HttpUtility.HtmlEncode(line)));
                return Results.Ok(new { success = true, logs = formattedLogs, logFile = Path.GetFileName(latestLog) });
            }
            
            return Results.Ok(new { success = true, logs = "No batch logs found" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> GetDriveScanStatus(HttpContext context, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var totalDrives = await dbContext.DriveConfigurations.CountAsync();
            var lastScan = await dbContext.DriveConfigurations
                .OrderByDescending(d => d.LastScanTime)
                .Select(d => d.LastScanTime)
                .FirstOrDefaultAsync();

            var scanPidFile = "drive-scan.pid";
            var isScanning = File.Exists(scanPidFile);

            // Get new documents since last hour (approximate)
            var oneHourAgo = DateTime.Now.AddHours(-1);
            var newDocuments = await dbContext.JumpDocuments
                .Where(d => d.LastScanned > oneHourAgo)
                .CountAsync();

            return Results.Ok(new
            {
                isScanning,
                totalDrives,
                lastScan = lastScan != default(DateTime) ? lastScan.ToString("g") : "Never",
                newDocuments
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> StartDriveScan(HttpContext context, JumpChainDbContext dbContext, AdminAuthService authService, IConfiguration configuration)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            // Update last scan time in configuration
            await UpdateLastScanTime(DateTime.UtcNow);
            
            // Check if scan is already running
            var scanPidFile = "drive-scan.pid";
            if (File.Exists(scanPidFile))
            {
                var existingPidText = File.ReadAllText(scanPidFile);
                if (int.TryParse(existingPidText, out int existingPid))
                {
                    try
                    {
                        var existingProcess = Process.GetProcessById(existingPid);
                        if (!existingProcess.HasExited)
                        {
                            return Results.Ok(new { success = false, message = "Drive scan is already running", pid = existingPid });
                        }
                    }
                    catch
                    {
                        File.Delete(scanPidFile);
                    }
                }
            }

            // Get drive count
            var drivesCount = await dbContext.DriveConfigurations.CountAsync();

            if (drivesCount == 0)
            {
                return Results.BadRequest(new { success = false, error = "No drives configured. Please configure drives first." });
            }

            // Detect platform and create appropriate script
            bool isWindows = OperatingSystem.IsWindows();
            string scriptPath;
            ProcessStartInfo startInfo;

            if (isWindows)
            {
                // Windows: PowerShell script
                var scanScript = @"
$scriptPath = '" + Directory.GetCurrentDirectory() + @"'
Set-Location $scriptPath

$timestamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$logFile = ""logs\drive-scan-$timestamp.log""

Write-Output ""Starting drive scan at $(Get-Date)"" | Out-File $logFile -Append

try {
    # Call the scan endpoint directly
    $response = Invoke-RestMethod -Uri 'http://localhost:5248/api/google-drive/scan-all' -Method POST
    Write-Output ""Scan completed: $($response | ConvertTo-Json)"" | Out-File $logFile -Append
} catch {
    Write-Output ""Error during scan: $($_.Exception.Message)"" | Out-File $logFile -Append
} finally {
    Remove-Item 'drive-scan.pid' -ErrorAction SilentlyContinue
}
";

                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "drive-scan.ps1");
                File.WriteAllText(scriptPath, scanScript);

                startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }
            else
            {
                // Linux: Bash script
                // Try to detect the actual listening URL, fallback to common defaults
                var apiUrl = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(';').FirstOrDefault()
                            ?? "http://localhost:5000";  // Kestrel default
                
                var scanScript = @"#!/bin/bash
cd " + Directory.GetCurrentDirectory() + $@"

timestamp=$(date +'%Y-%m-%d_%H-%M-%S')
logFile=""logs/drive-scan-$timestamp.log""

echo ""Starting drive scan at $(date)"" >> ""$logFile""

# Try the configured URL first, then fallback to common ports
curl -X POST {apiUrl}/api/google-drive/scan-all \
     -H 'Content-Type: application/json' \
     >> ""$logFile"" 2>&1

echo ""Scan completed at $(date)"" >> ""$logFile""
rm -f drive-scan.pid
";

                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "drive-scan.sh");
                File.WriteAllText(scriptPath, scanScript);
                
                // Make script executable on Linux
                Process.Start("chmod", $"+x \"{scriptPath}\"")?.WaitForExit();

                startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }

            var process = Process.Start(startInfo);
            if (process != null)
            {
                File.WriteAllText("drive-scan.pid", process.Id.ToString());
                var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }
                File.WriteAllText(Path.Combine(logsDir, "drive-scan-last-run.txt"), DateTime.Now.ToString());

                return Results.Ok(new { success = true, message = "Drive scan started", pid = process.Id, drivesCount });
            }

            return Results.BadRequest(new { success = false, error = "Failed to start scan process" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> StopDriveScan(HttpContext context, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var scanPidFile = "drive-scan.pid";
            if (File.Exists(scanPidFile))
            {
                var pidContent = File.ReadAllText(scanPidFile);
                if (int.TryParse(pidContent, out int pid))
                {
                    try
                    {
                        var process = Process.GetProcessById(pid);
                        process.Kill();
                        File.Delete(scanPidFile);
                        return Results.Ok(new { success = true, message = "Drive scan stopped" });
                    }
                    catch (Exception ex)
                    {
                        File.Delete(scanPidFile);
                        return Results.Ok(new { success = true, message = $"Scan process not found, cleaned up PID file: {ex.Message}" });
                    }
                }
            }

            return Results.Ok(new { success = true, message = "No drive scan found to stop" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get all folders for a specific drive
    /// </summary>
    private static async Task<IResult> GetDriveFolders(string driveName, HttpContext context, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var drive = await dbContext.DriveConfigurations
                .FirstOrDefaultAsync(d => d.DriveName == driveName);
            
            if (drive == null)
            {
                return Results.NotFound(new { success = false, error = $"Drive '{driveName}' not found" });
            }

            var folders = await dbContext.FolderConfigurations
                .Where(f => f.ParentDriveId == drive.Id)
                .OrderBy(f => f.FolderName)
                .Select(f => new
                {
                    folderId = f.FolderId,
                    folderName = f.FolderName,
                    resourceKey = f.ResourceKey
                })
                .ToListAsync();

            return Results.Ok(folders);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get all drive configurations with folder counts
    /// </summary>
    private static async Task<IResult> GetDriveConfigurations(HttpContext context, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var drives = await dbContext.DriveConfigurations
                .OrderBy(d => d.DriveName)
                .Select(d => new
                {
                    id = d.Id,
                    driveName = d.DriveName,
                    driveId = d.DriveId,
                    documentCount = d.DocumentCount,
                    lastScanTime = d.LastScanTime,
                    isActive = d.IsActive,
                    description = d.Description
                })
                .ToListAsync();

            // Get folder counts for each drive
            var driveData = new List<object>();
            foreach (var drive in drives)
            {
                var folderCount = await dbContext.FolderConfigurations
                    .Where(f => f.ParentDriveId == drive.id)
                    .CountAsync();

                driveData.Add(new
                {
                    drive.id,
                    drive.driveName,
                    drive.driveId,
                    drive.documentCount,
                    drive.lastScanTime,
                    drive.isActive,
                    drive.description,
                    folderCount
                });
            }

            return Results.Ok(new
            {
                success = true,
                drives = driveData
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetDriveConfigurations failed: {ex.Message}");
            Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            return Results.BadRequest(new { success = false, error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// Scan a single drive
    /// </summary>
    private static async Task<IResult> ScanSingleDrive(string driveName, HttpContext context, IGoogleDriveService driveService, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            // Get drive configuration to get driveId
            var drive = await dbContext.DriveConfigurations
                .FirstOrDefaultAsync(d => d.DriveName == driveName);

            if (drive == null)
            {
                return Results.NotFound(new { success = false, error = "Drive not found" });
            }

            var result = await driveService.ScanDriveAsync(drive.DriveId, driveName);
            return Results.Ok(new
            {
                success = true,
                message = $"Scan completed for {driveName}",
                newDocuments = result.Count()
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Refresh folder list for a drive
    /// </summary>
    private static async Task<IResult> RefreshDriveFolders(string driveName, HttpContext context, IGoogleDriveService driveService, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var drive = await dbContext.DriveConfigurations
                .FirstOrDefaultAsync(d => d.DriveName == driveName);

            if (drive == null)
            {
                return Results.NotFound(new { success = false, error = "Drive not found" });
            }

            var folders = await driveService.DiscoverFolderHierarchyAsync(drive.DriveId, drive.ResourceKey);
            
            int created = 0;
            int updated = 0;

            foreach (var folder in folders)
            {
                // Skip folders with null/empty names to prevent database constraint violations
                if (string.IsNullOrWhiteSpace(folder.folderName))
                {
                    continue;
                }

                var existing = await dbContext.FolderConfigurations
                    .FirstOrDefaultAsync(f => f.FolderId == folder.folderId && f.ParentDriveId == drive.Id);

                if (existing == null)
                {
                    dbContext.FolderConfigurations.Add(new FolderConfiguration
                    {
                        FolderId = folder.folderId,
                        FolderName = folder.folderName,
                        ParentDriveId = drive.Id,
                        ResourceKey = folder.resourceKey,
                        FolderPath = folder.folderName ?? string.Empty,
                        IsActive = true,
                        IsAutoDiscovered = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    created++;
                }
                else
                {
                    existing.FolderName = folder.folderName;
                    existing.ResourceKey = folder.resourceKey;
                    existing.FolderPath = folder.folderName ?? string.Empty;
                    existing.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
            }

            await dbContext.SaveChangesAsync();

            return Results.Ok(new
            {
                success = true,
                message = $"Discovered {folders.Count} folders",
                foldersDiscovered = folders.Count,
                foldersCreated = created,
                foldersUpdated = updated
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> ApplyCommunityGenreTags(HttpContext context, GenreTagService genreService, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            Console.WriteLine("Starting community genre tag application...");
            var (matched, tagged) = await genreService.ApplyGenreTagsFromCommunityList();
            Console.WriteLine($"Completed: {matched} documents matched, {tagged} new tags applied");
            
            return Results.Ok(new
            {
                success = true,
                matchedDocuments = matched,
                newTagsApplied = tagged,
                message = $"Applied {tagged} new genre tags to {matched} documents"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error applying genre tags: {ex.Message}");
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> ApplyCommunitySeriesTags(HttpContext context, SeriesTagService seriesService, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            Console.WriteLine("Starting community series tag application...");
            var (matched, tagged) = await seriesService.ApplySeriesTagsFromCommunityList();
            Console.WriteLine($"Completed: {matched} documents matched, {tagged} new tags applied");
            
            return Results.Ok(new
            {
                success = true,
                matchedDocuments = matched,
                newTagsApplied = tagged,
                message = $"Applied {tagged} new series tags to {matched} documents"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error applying series tags: {ex.Message}");
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> GetTextReviewQueue(HttpContext context, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var flaggedDocuments = await dbContext.JumpDocuments
                .Where(d => d.TextNeedsReview)
                .OrderByDescending(d => d.TextReviewFlaggedAt)
                .Select(d => new
                {
                    d.Id,
                    d.Name,
                    d.TextReviewFlaggedBy,
                    d.TextReviewFlaggedAt,
                    ExtractedTextLength = d.ExtractedText != null ? d.ExtractedText.Length : 0
                })
                .ToListAsync();

            return Results.Ok(new
            {
                success = true,
                documents = flaggedDocuments
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching text review queue: {ex.Message}");
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    // Helper method to validate session
    private static async Task<(bool valid, AdminUser? user)> ValidateSession(HttpContext context, AdminAuthService authService)
    {
        var sessionToken = context.Request.Cookies["admin_session"];
        if (string.IsNullOrEmpty(sessionToken))
        {
            return (false, null);
        }

        return await authService.ValidateSessionAsync(sessionToken);
    }

    /// <summary>
    /// Admin page for managing drives and folders with hierarchical view
    /// </summary>
    private static async Task<IResult> GetDriveManagementPage(HttpContext context, AdminAuthService authService, JumpChainDbContext dbContext)
    {
        // Check session authentication
        var (valid, user) = await ValidateSession(context, authService);
        
        if (!valid)
        {
            return Results.Redirect("/Admin/Login");
        }

        var username = user?.Username ?? "Admin";
        
        // Get all drives with their folder counts and document counts
        var drives = await dbContext.DriveConfigurations
            .OrderBy(d => d.DriveName)
            .ToListAsync();
        
        var driveData = new List<(DriveConfiguration drive, int folderCount, int docCount, DateTime? lastScan)>();
        
        foreach (var drive in drives)
        {
            var folderCount = await dbContext.FolderConfigurations
                .CountAsync(f => f.ParentDriveId == drive.Id);
            
            var docCount = await dbContext.JumpDocuments
                .CountAsync(d => d.SourceDrive == drive.DriveName);
            
            driveData.Add((drive, folderCount, docCount, drive.LastScanTime));
        }
        
        context.Response.ContentType = "text/html";
        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Drive Management - JumpChain Search</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        
        :root {{
            --bg-primary: #1a1a2e;
            --bg-secondary: #16213e;
            --bg-tertiary: #0f3460;
            --accent: #e94560;
            --accent-hover: #c93551;
            --text-primary: #eee;
            --text-secondary: #aaa;
            --success: #2ecc71;
            --warning: #f39c12;
            --danger: #e74c3c;
            --border: #2a2a4e;
        }}
        
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: var(--bg-primary);
            color: var(--text-primary);
            line-height: 1.6;
            min-height: 100vh;
        }}
        
        header {{
            background: var(--bg-secondary);
            border-bottom: 2px solid var(--accent);
            padding: 1rem 2rem;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}
        
        header h1 {{
            font-size: 1.5rem;
            display: flex;
            align-items: center;
            gap: 0.5rem;
        }}
        
        nav {{
            display: flex;
            gap: 1rem;
        }}
        
        nav a {{
            color: var(--text-secondary);
            text-decoration: none;
            padding: 0.5rem 1rem;
            border-radius: 4px;
            transition: all 0.3s;
        }}
        
        nav a:hover, nav a.active {{
            background: var(--bg-tertiary);
            color: var(--text-primary);
        }}
        
        main {{
            padding: 2rem;
            max-width: 1400px;
            margin: 0 auto;
        }}
        
        .drive-card {{
            background: var(--bg-secondary);
            border: 1px solid var(--border);
            border-radius: 8px;
            margin-bottom: 1rem;
            overflow: hidden;
        }}
        
        .drive-header {{
            padding: 1rem 1.5rem;
            display: flex;
            justify-content: space-between;
            align-items: center;
            cursor: pointer;
            transition: background 0.2s;
        }}
        
        .drive-header:hover {{
            background: var(--bg-tertiary);
        }}
        
        .drive-info {{
            flex: 1;
        }}
        
        .drive-name {{
            font-size: 1.2rem;
            font-weight: 600;
            margin-bottom: 0.5rem;
        }}
        
        .drive-stats {{
            display: flex;
            gap: 2rem;
            color: var(--text-secondary);
            font-size: 0.9rem;
        }}
        
        .drive-stat {{
            display: flex;
            align-items: center;
            gap: 0.5rem;
        }}
        
        .drive-actions {{
            display: flex;
            gap: 0.5rem;
        }}
        
        .btn {{
            padding: 0.5rem 1rem;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-size: 0.9rem;
            transition: all 0.3s;
            text-decoration: none;
            display: inline-block;
        }}
        
        .btn-primary {{
            background: var(--accent);
            color: white;
        }}
        
        .btn-primary:hover {{
            background: var(--accent-hover);
        }}
        
        .btn-secondary {{
            background: var(--bg-tertiary);
            color: var(--text-primary);
        }}
        
        .btn-secondary:hover {{
            background: #0a2540;
        }}
        
        .folder-list {{
            display: none;
            padding: 1rem 1.5rem;
            background: var(--bg-primary);
            border-top: 1px solid var(--border);
            max-height: 400px;
            overflow-y: auto;
        }}
        
        .folder-list.expanded {{
            display: block;
        }}
        
        .folder-item {{
            padding: 0.5rem;
            margin-bottom: 0.25rem;
            background: var(--bg-secondary);
            border-radius: 4px;
            font-size: 0.9rem;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}
        
        .folder-name {{
            color: var(--text-secondary);
        }}
        
        .expand-icon {{
            transition: transform 0.3s;
        }}
        
        .expand-icon.rotated {{
            transform: rotate(90deg);
        }}
        
        .loading {{
            display: inline-block;
            width: 16px;
            height: 16px;
            border: 2px solid var(--border);
            border-top-color: var(--accent);
            border-radius: 50%;
            animation: spin 0.8s linear infinite;
        }}
        
        @keyframes spin {{
            to {{ transform: rotate(360deg); }}
        }}
        
        .scan-status {{
            margin-top: 0.5rem;
            padding: 0.5rem;
            background: var(--bg-tertiary);
            border-radius: 4px;
            font-size: 0.85rem;
            display: none;
        }}
        
        .scan-status.active {{
            display: block;
        }}
    </style>
</head>
<body>
    <header>
        <h1>
            <span>🔧</span>
            Drive Management
        </h1>
        <nav>
            <a href=""/api/admin"">Dashboard</a>
            <a href=""/api/admin/drives"" class=""active"">Drives</a>
            <a href=""/"">Search</a>
        </nav>
    </header>
    
    <main>
        <section>
            <h2 style=""margin-bottom: 1.5rem;"">Configured Drives</h2>
            
            {string.Join("\n", driveData.Select((data, index) => $@"
            <article class=""drive-card"">
                <div class=""drive-header"" onclick=""toggleFolders({index})"">
                    <div class=""drive-info"">
                        <h3 class=""drive-name"">
                            <span class=""expand-icon"" id=""icon-{index}"">▶</span>
                            {data.drive.DriveName}
                        </h3>
                        <div class=""drive-stats"">
                            <div class=""drive-stat"">
                                <span>📊</span>
                                <span>{data.docCount:N0} documents</span>
                            </div>
                            {(data.folderCount > 0 ? $@"
                            <div class=""drive-stat"">
                                <span>📁</span>
                                <span>{data.folderCount} folders</span>
                            </div>" : "")}
                            <div class=""drive-stat"">
                                <span>⏱️</span>
                                <span>Last scan: {(data.lastScan.HasValue ? $"{(DateTime.Now - data.lastScan.Value).TotalHours:F1} hours ago" : "Never")}</span>
                            </div>
                        </div>
                    </div>
                    <div class=""drive-actions"" onclick=""event.stopPropagation()"">
                        <button class=""btn btn-primary"" onclick=""scanDrive('{data.drive.DriveName}')"">Scan Drive</button>
                        <button class=""btn btn-secondary"" onclick=""refreshFolders('{data.drive.DriveName}')"">Refresh Folders</button>
                    </div>
                </div>
                <div class=""folder-list"" id=""folders-{index}"">
                    <div class=""loading""></div>
                    <p style=""color: var(--text-secondary); margin-left: 2rem;"">Loading folders...</p>
                </div>
                <div class=""scan-status"" id=""scan-status-{index}""></div>
            </article>"))}
        </section>
    </main>
    
    <script>
        async function toggleFolders(index) {{
            const folderList = document.getElementById(`folders-${{index}}`);
            const icon = document.getElementById(`icon-${{index}}`);
            
            if (folderList.classList.contains('expanded')) {{
                folderList.classList.remove('expanded');
                icon.classList.remove('rotated');
            }} else {{
                // Load folders if not already loaded
                if (folderList.querySelector('.loading')) {{
                    const driveName = folderList.closest('.drive-card').querySelector('.drive-name').textContent.trim().replace('▶', '').trim();
                    await loadFolders(index, driveName);
                }}
                folderList.classList.add('expanded');
                icon.classList.add('rotated');
            }}
        }}
        
        async function loadFolders(index, driveName) {{
            const folderList = document.getElementById(`folders-${{index}}`);
            
            try {{
                const response = await fetch('/api/admin/drives/' + encodeURIComponent(driveName) + '/folders');
                const folders = await response.json();
                
                if (folders.length === 0) {{
                    folderList.innerHTML = '<p style=""color: var(--text-secondary); padding: 1rem;"">No subfolders (flat drive structure)</p>';
                }} else {{
                    folderList.innerHTML = folders.map(f => `
                        <div class=""folder-item"">
                            <span class=""folder-name"">📁 ${{f.folderName}}</span>
                            <span style=""color: var(--text-secondary); font-size: 0.85rem;"">ID: ${{f.folderId.substring(0, 12)}}...</span>
                        </div>
                    `).join('');
                }}
            }} catch (error) {{
                folderList.innerHTML = '<p style=""color: var(--danger); padding: 1rem;"">Error loading folders: ' + error.message + '</p>';
            }}
        }}
        
        async function scanDrive(driveName) {{
            const statusDiv = Array.from(document.querySelectorAll('.drive-card')).find(card => 
                card.querySelector('.drive-name').textContent.includes(driveName)
            )?.querySelector('.scan-status');
            
            if (statusDiv) {{
                statusDiv.classList.add('active');
                statusDiv.innerHTML = '<div class=""loading"" style=""display: inline-block; margin-right: 0.5rem;""></div> Scanning drive...';
            }}
            
            try {{
                const response = await fetch('/api/google-drive/scan-drive/' + encodeURIComponent(driveName), {{
                    method: 'POST'
                }});
                const result = await response.json();
                
                if (result.success) {{
                    if (statusDiv) {{
                        statusDiv.innerHTML = `✅ Scan complete! Found ${{result.documentsFound}} documents (${{result.newDocuments}} new)`;
                        setTimeout(() => statusDiv.classList.remove('active'), 5000);
                    }}
                    setTimeout(() => location.reload(), 2000);
                }} else {{
                    if (statusDiv) {{
                        statusDiv.innerHTML = `❌ Scan failed: ${{result.error || 'Unknown error'}}`;
                    }}
                }}
            }} catch (error) {{
                if (statusDiv) {{
                    statusDiv.innerHTML = `❌ Error: ${{error.message}}`;
                }}
            }}
        }}
        
        async function refreshFolders(driveName) {{
            const statusDiv = Array.from(document.querySelectorAll('.drive-card')).find(card => 
                card.querySelector('.drive-name').textContent.includes(driveName)
            )?.querySelector('.scan-status');
            
            if (statusDiv) {{
                statusDiv.classList.add('active');
                statusDiv.innerHTML = '<div class=""loading"" style=""display: inline-block; margin-right: 0.5rem;""></div> Discovering folders...';
            }}
            
            try {{
                const response = await fetch('/api/google-drive/save-folders/' + encodeURIComponent(driveName), {{
                    method: 'POST'
                }});
                const result = await response.json();
                
                if (result.success) {{
                    if (statusDiv) {{
                        statusDiv.innerHTML = `✅ Folder discovery complete! ${{result.foldersDiscovered}} folders (${{result.foldersCreated}} new, ${{result.foldersUpdated}} updated)`;
                        setTimeout(() => statusDiv.classList.remove('active'), 5000);
                    }}
                    setTimeout(() => location.reload(), 2000);
                }} else {{
                    if (statusDiv) {{
                        statusDiv.innerHTML = `❌ Discovery failed: ${{result.error || 'Unknown error'}}`;
                    }}
                }}
            }} catch (error) {{
                if (statusDiv) {{
                    statusDiv.innerHTML = `❌ Error: ${{error.message}}`;
                }}
            }}
        }}
    </script>
</body>
</html>";
        
        await context.Response.WriteAsync(html);
        return Results.Empty;
    }
    
    // System Management Endpoints
    
    private static async Task<IResult> GetCacheTTL(HttpContext context, IConfiguration configuration, AdminAuthService authService)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Unauthorized();
        
        var minutes = configuration.GetValue<int>("CacheSettings:SearchCacheDurationMinutes", 5);
        return Results.Ok(new { minutes });
    }
    
    private static async Task<IResult> UpdateCacheTTL(HttpContext context, IConfiguration configuration, AdminAuthService authService, int minutes)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Unauthorized();
        
        if (minutes < 1 || minutes > 60)
            return Results.BadRequest(new { success = false, message = "Minutes must be between 1 and 60" });
        
        try
        {
            // Update runtime cache duration
            SearchEndpointsOptimized.SetCacheDuration(minutes);
            
            // Update appsettings file for persistence
            var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var json = await File.ReadAllTextAsync(appsettingsPath);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
            
            using var stream = new MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
            
            writer.WriteStartObject();
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Name == "CacheSettings")
                {
                    writer.WriteStartObject("CacheSettings");
                    writer.WriteNumber("SearchCacheDurationMinutes", minutes);
                    writer.WriteNumber("TagCacheDurationMinutes", 
                        property.Value.TryGetProperty("TagCacheDurationMinutes", out var tagMinutes) 
                            ? tagMinutes.GetInt32() 
                            : 10);
                    writer.WriteEndObject();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            await writer.FlushAsync();
            
            await File.WriteAllBytesAsync(appsettingsPath, stream.ToArray());
            
            return Results.Ok(new { success = true, minutes });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { success = false, message = ex.Message });
        }
    }
    
    /// <summary>
    /// Refresh the document count cache used by the front page
    /// </summary>
    private static async Task<IResult> RefreshDocumentCount(
        HttpContext context,
        AdminAuthService authService,
        IDocumentCountService documentCountService)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Ok(new { success = false, error = "Session expired - please log in again" });
        
        try
        {
            await documentCountService.RefreshCountAsync();
            var currentCount = await documentCountService.GetCountAsync();
            
            return Results.Ok(new { 
                success = true, 
                currentCount,
                message = $"Document count refreshed: {currentCount:N0}" 
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }
    
    private static async Task<IResult> GetScanSchedule(HttpContext context, IConfiguration configuration, AdminAuthService authService)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Unauthorized();
        
        var enabled = configuration.GetValue<bool>("ScanScheduling:Enabled", false);
        var intervalHours = configuration.GetValue<int>("ScanScheduling:IntervalHours", 24);
        var lastScanTimeStr = configuration.GetValue<string>("ScanScheduling:LastScanTime");
        var nextScanTimeStr = configuration.GetValue<string>("ScanScheduling:NextScheduledScan");
        
        DateTime? lastScanTime = null;
        if (!string.IsNullOrEmpty(lastScanTimeStr) && DateTime.TryParse(lastScanTimeStr, out var parsedLast))
        {
            lastScanTime = parsedLast;
        }
        
        DateTime? nextScheduledScan = null;
        if (!string.IsNullOrEmpty(nextScanTimeStr) && DateTime.TryParse(nextScanTimeStr, out var parsedNext))
        {
            nextScheduledScan = parsedNext;
        }
        
        return Results.Ok(new
        {
            enabled,
            intervalHours,
            lastScanTime,
            nextScheduledScan
        });
    }
    
    private static async Task<IResult> UpdateScanSchedule(HttpContext context, IConfiguration configuration, AdminAuthService authService)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Unauthorized();
        
        var request = await context.Request.ReadFromJsonAsync<ScanScheduleRequest>();
        if (request == null) return Results.BadRequest(new { success = false, message = "Invalid request" });
        
        try
        {
            // Update appsettings file
            var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var json = await File.ReadAllTextAsync(appsettingsPath);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
            
            using var stream = new MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
            
            writer.WriteStartObject();
            bool scanSchedulingWritten = false;
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Name == "ScanScheduling")
                {
                    scanSchedulingWritten = true;
                    writer.WriteStartObject("ScanScheduling");
                    writer.WriteBoolean("Enabled", request.Enabled);
                    writer.WriteNumber("IntervalHours", request.IntervalHours);
                    
                    // Preserve LastScanTime if it exists
                    if (property.Value.TryGetProperty("LastScanTime", out var lastScanTime) && lastScanTime.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        writer.WriteString("LastScanTime", lastScanTime.GetString());
                    }
                    else
                    {
                        writer.WriteNull("LastScanTime");
                    }
                    
                    // Calculate NextScheduledScan based on current state
                    DateTime? nextScheduledScan = null;
                    if (request.Enabled)
                    {
                        // If LastScanTime exists, schedule from there. Otherwise schedule from now.
                        if (property.Value.TryGetProperty("LastScanTime", out var lastScanProp) && 
                            lastScanProp.ValueKind != System.Text.Json.JsonValueKind.Null &&
                            DateTime.TryParse(lastScanProp.GetString(), out var lastScan))
                        {
                            nextScheduledScan = lastScan.AddHours(request.IntervalHours);
                        }
                        else
                        {
                            // No last scan - schedule for the configured interval from now
                            nextScheduledScan = DateTime.UtcNow.AddHours(request.IntervalHours);
                        }
                    }
                    
                    if (nextScheduledScan.HasValue)
                    {
                        writer.WriteString("NextScheduledScan", nextScheduledScan.Value.ToString("o"));
                    }
                    else
                    {
                        writer.WriteNull("NextScheduledScan");
                    }
                    
                    writer.WriteEndObject();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }
            
            // If ScanScheduling section didn't exist, add it now
            if (!scanSchedulingWritten)
            {
                writer.WriteStartObject("ScanScheduling");
                writer.WriteBoolean("Enabled", request.Enabled);
                writer.WriteNumber("IntervalHours", request.IntervalHours);
                writer.WriteNull("LastScanTime");
                
                if (request.Enabled)
                {
                    var nextScheduledScan = DateTime.UtcNow.AddHours(request.IntervalHours);
                    writer.WriteString("NextScheduledScan", nextScheduledScan.ToString("o"));
                }
                else
                {
                    writer.WriteNull("NextScheduledScan");
                }
                
                writer.WriteEndObject();
            }
            
            writer.WriteEndObject();
            await writer.FlushAsync();
            
            await File.WriteAllBytesAsync(appsettingsPath, stream.ToArray());
            
            return Results.Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { success = false, message = ex.Message });
        }
    }
    
    // Helper method to update last scan time
    private static async Task UpdateLastScanTime(DateTime scanTime)
    {
        try
        {
            var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var json = await File.ReadAllTextAsync(appsettingsPath);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
            
            using var stream = new MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
            
            writer.WriteStartObject();
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Name == "ScanScheduling")
                {
                    writer.WriteStartObject("ScanScheduling");
                    
                    // Preserve existing settings
                    var isEnabled = property.Value.TryGetProperty("Enabled", out var enabled) ? enabled.GetBoolean() : false;
                    var intervalHours = property.Value.TryGetProperty("IntervalHours", out var interval) ? interval.GetInt32() : 24;
                    
                    writer.WriteBoolean("Enabled", isEnabled);
                    writer.WriteNumber("IntervalHours", intervalHours);
                    
                    // Update LastScanTime
                    writer.WriteString("LastScanTime", scanTime.ToString("o"));
                    
                    // Calculate and set NextScheduledScan
                    if (isEnabled)
                    {
                        var nextScan = scanTime.AddHours(intervalHours);
                        writer.WriteString("NextScheduledScan", nextScan.ToString("o"));
                    }
                    else
                    {
                        writer.WriteNull("NextScheduledScan");
                    }
                    
                    writer.WriteEndObject();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            await writer.FlushAsync();
            
            await File.WriteAllBytesAsync(appsettingsPath, stream.ToArray());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update last scan time: {ex.Message}");
        }
    }
    
    private static async Task<IResult> SetNextScheduledScan(HttpContext context, IConfiguration configuration, AdminAuthService authService)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Unauthorized();
        
        try
        {
            var intervalHours = configuration.GetValue<int>("ScanScheduling:IntervalHours", 24);
            var nextScan = DateTime.UtcNow.AddHours(intervalHours);
            
            var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var json = await File.ReadAllTextAsync(appsettingsPath);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
            
            using var stream = new MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
            
            writer.WriteStartObject();
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Name == "ScanScheduling")
                {
                    writer.WriteStartObject("ScanScheduling");
                    
                    // Preserve all existing settings
                    writer.WriteBoolean("Enabled", 
                        property.Value.TryGetProperty("Enabled", out var enabled) ? enabled.GetBoolean() : false);
                    writer.WriteNumber("IntervalHours", intervalHours);
                    
                    if (property.Value.TryGetProperty("LastScanTime", out var lastScan) && lastScan.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        writer.WriteString("LastScanTime", lastScan.GetString());
                    }
                    else
                    {
                        writer.WriteNull("LastScanTime");
                    }
                    
                    // Set NextScheduledScan to now + interval
                    writer.WriteString("NextScheduledScan", nextScan.ToString("o"));
                    
                    writer.WriteEndObject();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            await writer.FlushAsync();
            
            await File.WriteAllBytesAsync(appsettingsPath, stream.ToArray());
            
            return Results.Ok(new { success = true, nextScheduledScan = nextScan });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { success = false, message = ex.Message });
        }
    }
    
    /// <summary>
    /// Initialize scan schedule after deployment - localhost only, no auth required
    /// </summary>
    private static async Task<IResult> InitializeScanSchedule(HttpContext context, IConfiguration configuration)
    {
        // Only allow from localhost for security
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null || (!remoteIp.ToString().StartsWith("127.") && !remoteIp.ToString().StartsWith("::1")))
        {
            return Results.Forbid();
        }
        
        try
        {
            var intervalHours = configuration.GetValue<int>("ScanScheduling:IntervalHours", 1);
            var nextScan = DateTime.UtcNow.AddHours(intervalHours);
            
            var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var json = await File.ReadAllTextAsync(appsettingsPath);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
            
            using var stream = new MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
            
            writer.WriteStartObject();
            bool scanSchedulingWritten = false;
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Name == "ScanScheduling")
                {
                    scanSchedulingWritten = true;
                    writer.WriteStartObject("ScanScheduling");
                    writer.WriteBoolean("Enabled", true);
                    writer.WriteNumber("IntervalHours", intervalHours);
                    writer.WriteNull("LastScanTime");
                    writer.WriteString("NextScheduledScan", nextScan.ToString("o"));
                    writer.WriteEndObject();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }
            
            // If ScanScheduling section didn't exist, add it now
            if (!scanSchedulingWritten)
            {
                writer.WriteStartObject("ScanScheduling");
                writer.WriteBoolean("Enabled", true);
                writer.WriteNumber("IntervalHours", intervalHours);
                writer.WriteNull("LastScanTime");
                writer.WriteString("NextScheduledScan", nextScan.ToString("o"));
                writer.WriteEndObject();
            }
            
            writer.WriteEndObject();
            await writer.FlushAsync();
            
            await File.WriteAllBytesAsync(appsettingsPath, stream.ToArray());
            
            return Results.Ok(new { success = true, enabled = true, nextScheduledScan = nextScan, message = "Scan schedule initialized" });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { success = false, message = ex.Message });
        }
    }
    
    // Account Management Endpoints
    
    private static async Task<IResult> ChangeUsername(HttpContext context, AdminAuthService authService, JumpChainDbContext dbContext)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid) return Results.Json(new { success = false, message = "Unauthorized" });
        
        var request = await context.Request.ReadFromJsonAsync<ChangeUsernameRequest>();
        if (request == null) return Results.Json(new { success = false, message = "Invalid request" });
        
        // Verify current password
        var admin = await dbContext.AdminUsers.FirstOrDefaultAsync(a => a.Username == user!.Username);
        if (admin == null) return Results.Json(new { success = false, message = "Admin user not found" });
        
        if (!authService.VerifyPassword(request.CurrentPassword, admin.PasswordHash, admin.Salt))
        {
            return Results.Json(new { success = false, message = "Current password is incorrect" });
        }
        
        // Check if new username already exists
        var existingUser = await dbContext.AdminUsers.FirstOrDefaultAsync(a => a.Username == request.NewUsername);
        if (existingUser != null && existingUser.Id != admin.Id)
        {
            return Results.Json(new { success = false, message = "Username already exists" });
        }
        
        // Update username
        admin.Username = request.NewUsername;
        await dbContext.SaveChangesAsync();
        
        return Results.Json(new { success = true, message = "Username updated successfully" });
    }
    
    private static async Task<IResult> ChangePassword(HttpContext context, AdminAuthService authService, JumpChainDbContext dbContext)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid) return Results.Json(new { success = false, message = "Unauthorized" });
        
        var request = await context.Request.ReadFromJsonAsync<ChangePasswordRequest>();
        if (request == null) return Results.Json(new { success = false, message = "Invalid request" });
        
        if (request.NewPassword.Length < 8)
        {
            return Results.Json(new { success = false, message = "Password must be at least 8 characters" });
        }
        
        // Use existing ChangePasswordAsync which handles verification, hashing, and session invalidation
        var success = await authService.ChangePasswordAsync(user!.Username, request.CurrentPassword, request.NewPassword);
        
        if (!success)
        {
            return Results.Json(new { success = false, message = "Current password is incorrect" });
        }
        
        return Results.Json(new { success = true, message = "Password updated successfully" });
    }
    
    private static async Task<IResult> Logout(HttpContext context, AdminAuthService authService)
    {
        // Get session token from cookie (correct cookie name is "admin_session")
        if (context.Request.Cookies.TryGetValue("admin_session", out var sessionToken))
        {
            await authService.LogoutAsync(sessionToken);
        }
        
        // Delete the cookie with options that match how it was created
        context.Response.Cookies.Delete("admin_session", new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        
        return Results.Redirect("/Admin/Login");
    }
    
    private static Task<IResult> GetSystemDiagnostic(HttpContext context, IServiceProvider serviceProvider, AdminAuthService authService)
    {
        // Check if scanner service is registered
        var hostedServices = serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        var services = hostedServices.Select(s => s.GetType().Name).ToList();
        
        var scanSchedulerExists = services.Any(s => s.Contains("ScanScheduler"));
        
        var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var appsettingsExists = File.Exists(appsettingsPath);
        
        var diagnostic = new
        {
            hostedServices = services,
            scanSchedulerRegistered = scanSchedulerExists,
            appsettingsPath,
            appsettingsExists,
            baseDirectory = AppContext.BaseDirectory,
            currentDirectory = Directory.GetCurrentDirectory()
        };
        
        return Task.FromResult(Results.Ok(diagnostic));
    }
    
    /// <summary>
    /// Check if series-mappings.json is loaded correctly and show its contents
    /// </summary>
    private static Task<IResult> CheckSeriesMappings(HttpContext context)
    {
        var seriesMappingsPath = Path.Combine(AppContext.BaseDirectory, "series-mappings.json");
        var fileExists = File.Exists(seriesMappingsPath);
        
        Dictionary<string, List<string>>? mappings = null;
        string? error = null;
        
        if (fileExists)
        {
            try
            {
                var json = File.ReadAllText(seriesMappingsPath);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                
                mappings = new Dictionary<string, List<string>>();
                
                if (doc.RootElement.TryGetProperty("seriesMappings", out var seriesMappingsElement))
                {
                    foreach (var series in seriesMappingsElement.EnumerateObject())
                    {
                        var patterns = new List<string>();
                        foreach (var pattern in series.Value.EnumerateArray())
                        {
                            patterns.Add(pattern.GetString() ?? "");
                        }
                        mappings[series.Name] = patterns;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }
        
        var result = new
        {
            path = seriesMappingsPath,
            fileExists,
            error,
            seriesCount = mappings?.Count ?? 0,
            series = mappings?.Keys.ToList() ?? new List<string>(),
            baseDirectory = AppContext.BaseDirectory,
            currentDirectory = Directory.GetCurrentDirectory()
        };
        
        return Task.FromResult(Results.Ok(result));
    }
    
    /// <summary>
    /// Search for tags by name (for autocomplete)
    /// Returns distinct tag names with their categories and usage count
    /// </summary>
    /// <summary>
    /// Get the list of valid tag categories
    /// Returns the authoritative list from TagCategory constants
    /// </summary>
    private static IResult GetTagCategories(HttpContext context)
    {
        return Results.Ok(new
        {
            success = true,
            categories = TagCategory.UserFacing
        });
    }

    /// <summary>
    /// Search for existing tags by name (case-insensitive)
    /// Returns matching tags with their current category and document count
    /// </summary>
    private static async Task<IResult> SearchTags(
        HttpContext context,
        JumpChainDbContext dbContext,
        string? query = null)
    {
        try
        {
            var searchQuery = query?.Trim() ?? "";
            
            // Get distinct tags matching the search query (case-insensitive)
            var tags = await dbContext.DocumentTags
                .Where(t => string.IsNullOrEmpty(searchQuery) || EF.Functions.Like(t.TagName, $"%{searchQuery}%"))
                .GroupBy(t => new { t.TagName, t.TagCategory })
                .Select(g => new {
                    tagName = g.Key.TagName,
                    tagCategory = g.Key.TagCategory,
                    documentCount = g.Count()
                })
                .OrderByDescending(t => t.documentCount)
                .Take(50)
                .ToListAsync();

            return Results.Ok(new { 
                success = true, 
                tags 
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                message = "Failed to search tags", 
                error = ex.Message 
            });
        }
    }
    
    /// <summary>
    /// Recategorize all instances of a tag across all documents
    /// Updates DocumentTags and associated ApprovedTagRules
    /// </summary>
    private static async Task<IResult> RecategorizeTag(
        HttpContext context,
        JumpChainDbContext dbContext,
        RecategorizeTagRequest request)
    {
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(request.TagName) || 
                string.IsNullOrWhiteSpace(request.OldCategory) || 
                string.IsNullOrWhiteSpace(request.NewCategory))
            {
                return Results.BadRequest(new { 
                    success = false, 
                    message = "TagName, OldCategory, and NewCategory are required" 
                });
            }

            // Find all DocumentTags with this tag name and category
            var tagsToUpdate = await dbContext.DocumentTags
                .Where(t => t.TagName == request.TagName && t.TagCategory == request.OldCategory)
                .ToListAsync();

            if (tagsToUpdate.Count == 0)
            {
                return Results.NotFound(new { 
                    success = false, 
                    message = $"No tags found with name '{request.TagName}' in category '{request.OldCategory}'" 
                });
            }

            // Update all DocumentTags
            foreach (var tag in tagsToUpdate)
            {
                tag.TagCategory = request.NewCategory;
            }

            // Find and update associated ApprovedTagRules
            var rulesToUpdate = await dbContext.ApprovedTagRules
                .Where(r => r.TagName == request.TagName && r.TagCategory == request.OldCategory)
                .ToListAsync();

            foreach (var rule in rulesToUpdate)
            {
                rule.TagCategory = request.NewCategory;
            }

            await dbContext.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = $"Recategorized tag '{request.TagName}' from '{request.OldCategory}' to '{request.NewCategory}'",
                documentTagsUpdated = tagsToUpdate.Count,
                approvedRulesUpdated = rulesToUpdate.Count
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                message = "Failed to recategorize tag", 
                error = ex.Message 
            });
        }
    }
    
    /// <summary>
    /// Get OCR quality analytics for charts:
    /// - Document count vs. text length
    /// - Document count vs. OCR quality (parsed from ExtractionMethod)
    /// </summary>
    private static async Task<IResult> GetOcrQualityAnalytics(
        HttpContext context,
        JumpChainDbContext dbContext,
        AdminAuthService authService)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Unauthorized();

        try
        {
            // Get all documents with extracted text
            var documents = await dbContext.JumpDocuments
                .Where(d => !string.IsNullOrEmpty(d.ExtractedText))
                .Select(d => new
                {
                    TextLength = d.ExtractedText!.Length,
                    ExtractionMethod = d.ExtractionMethod ?? ""
                })
                .ToListAsync();

            // Text length distribution (bucket by character count)
            var textLengthBuckets = new Dictionary<string, int>
            {
                ["0-100"] = 0,
                ["100-500"] = 0,
                ["500-1000"] = 0,
                ["1000-2500"] = 0,
                ["2500-5000"] = 0,
                ["5000-10000"] = 0,
                ["10000-25000"] = 0,
                ["25000-50000"] = 0,
                ["50000+"] = 0
            };

            foreach (var doc in documents)
            {
                var len = doc.TextLength;
                if (len < 100) textLengthBuckets["0-100"]++;
                else if (len < 500) textLengthBuckets["100-500"]++;
                else if (len < 1000) textLengthBuckets["500-1000"]++;
                else if (len < 2500) textLengthBuckets["1000-2500"]++;
                else if (len < 5000) textLengthBuckets["2500-5000"]++;
                else if (len < 10000) textLengthBuckets["5000-10000"]++;
                else if (len < 25000) textLengthBuckets["10000-25000"]++;
                else if (len < 50000) textLengthBuckets["25000-50000"]++;
                else textLengthBuckets["50000+"]++;
            }

            // OCR quality distribution (parse from ExtractionMethod)
            var ocrQualityBuckets = new Dictionary<string, int>
            {
                ["0.0-0.1"] = 0,
                ["0.1-0.2"] = 0,
                ["0.2-0.3"] = 0,
                ["0.3-0.4"] = 0,
                ["0.4-0.5"] = 0,
                ["0.5-0.6"] = 0,
                ["0.6-0.7"] = 0,
                ["0.7-0.8"] = 0,
                ["0.8-0.9"] = 0,
                ["0.9-1.0"] = 0,
                ["No OCR"] = 0
            };

            foreach (var doc in documents)
            {
                var quality = ParseOcrQuality(doc.ExtractionMethod);
                
                if (quality.HasValue)
                {
                    var q = quality.Value;
                    if (q < 0.1) ocrQualityBuckets["0.0-0.1"]++;
                    else if (q < 0.2) ocrQualityBuckets["0.1-0.2"]++;
                    else if (q < 0.3) ocrQualityBuckets["0.2-0.3"]++;
                    else if (q < 0.4) ocrQualityBuckets["0.3-0.4"]++;
                    else if (q < 0.5) ocrQualityBuckets["0.4-0.5"]++;
                    else if (q < 0.6) ocrQualityBuckets["0.5-0.6"]++;
                    else if (q < 0.7) ocrQualityBuckets["0.6-0.7"]++;
                    else if (q < 0.8) ocrQualityBuckets["0.7-0.8"]++;
                    else if (q < 0.9) ocrQualityBuckets["0.8-0.9"]++;
                    else ocrQualityBuckets["0.9-1.0"]++;
                }
                else
                {
                    ocrQualityBuckets["No OCR"]++;
                }
            }

            return Results.Ok(new
            {
                success = true,
                totalDocuments = documents.Count,
                textLengthDistribution = textLengthBuckets,
                ocrQualityDistribution = ocrQualityBuckets
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting OCR analytics: {ex.Message}");
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> CalculateReprocessCount(
        HttpContext context,
        JumpChainDbContext dbContext,
        AdminAuthService authService,
        int textThreshold,
        double qualityThreshold)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Unauthorized();

        try
        {
            var query = dbContext.JumpDocuments.AsQueryable();
            
            // Documents with short text OR low quality OCR
            var count = await query
                .Where(d => 
                    (!string.IsNullOrEmpty(d.ExtractedText) && d.ExtractedText!.Length <= textThreshold) ||
                    (d.ExtractionMethod != null && d.ExtractionMethod.Contains("tesseract_ocr")))
                .CountAsync();
            
            // Further filter by OCR quality threshold (needs to be done in memory due to parsing)
            var ocrDocs = await query
                .Where(d => d.ExtractionMethod != null && d.ExtractionMethod.Contains("tesseract_ocr"))
                .Select(d => new { d.Id, d.ExtractionMethod, TextLength = d.ExtractedText != null ? d.ExtractedText.Length : 0 })
                .ToListAsync();
            
            var lowQualityOcrCount = ocrDocs
                .Where(d => 
                {
                    var quality = ParseOcrQuality(d.ExtractionMethod);
                    return quality.HasValue && quality.Value <= qualityThreshold;
                })
                .Count();
            
            var shortTextCount = await query
                .Where(d => !string.IsNullOrEmpty(d.ExtractedText) && d.ExtractedText!.Length <= textThreshold)
                .CountAsync();
            
            // Count unique documents that meet either criteria
            var reprocessIds = await query
                .Where(d => 
                    (!string.IsNullOrEmpty(d.ExtractedText) && d.ExtractedText!.Length <= textThreshold) ||
                    (d.ExtractionMethod != null && d.ExtractionMethod.Contains("tesseract_ocr")))
                .Select(d => d.Id)
                .ToListAsync();
            
            var finalCount = reprocessIds
                .Where(id =>
                {
                    var doc = ocrDocs.FirstOrDefault(d => d.Id == id);
                    if (doc == null) return true; // Include short text docs without OCR
                    
                    var quality = ParseOcrQuality(doc.ExtractionMethod);
                    return doc.TextLength <= textThreshold || (quality.HasValue && quality.Value <= qualityThreshold);
                })
                .Distinct()
                .Count();

            return Results.Ok(new
            {
                success = true,
                count = finalCount
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calculating reprocess count: {ex.Message}");
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get progress of ongoing reprocessing operation
    /// Returns count of documents still needing text extraction (ExtractedText is null)
    /// </summary>
    private static async Task<IResult> GetReprocessProgress(
        HttpContext context,
        JumpChainDbContext dbContext,
        AdminAuthService authService)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Unauthorized();

        try
        {
            // Count documents with null ExtractedText (queued for processing)
            var remaining = await dbContext.JumpDocuments
                .Where(d => d.ExtractedText == null)
                .CountAsync();

            // Get total document count for context
            var total = await dbContext.JumpDocuments.CountAsync();

            return Results.Ok(new
            {
                success = true,
                remaining = remaining,
                processed = total - remaining,
                totalQueued = remaining, // For backwards compatibility with UI
                percentComplete = total > 0 ? Math.Round(((double)(total - remaining) / total) * 100, 1) : 100
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting reprocess progress: {ex.Message}");
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static async Task<IResult> StartBatchReprocess(
        HttpContext context,
        JumpChainDbContext dbContext,
        AdminAuthService authService,
        ReprocessRequest request)
    {
        var (valid, _) = await ValidateSession(context, authService);
        if (!valid) return Results.Unauthorized();

        try
        {
            // Find documents matching criteria
            var query = dbContext.JumpDocuments.AsQueryable();
            
            var reprocessIds = await query
                .Where(d => 
                    (!string.IsNullOrEmpty(d.ExtractedText) && d.ExtractedText!.Length <= request.TextThreshold) ||
                    (d.ExtractionMethod != null && d.ExtractionMethod.Contains("tesseract_ocr")))
                .Select(d => new { d.Id, d.ExtractionMethod, TextLength = d.ExtractedText != null ? d.ExtractedText.Length : 0 })
                .ToListAsync();
            
            var filteredIds = reprocessIds
                .Where(d =>
                {
                    var quality = ParseOcrQuality(d.ExtractionMethod);
                    return d.TextLength <= request.TextThreshold || (quality.HasValue && quality.Value <= request.QualityThreshold);
                })
                .Select(d => d.Id)
                .Distinct()
                .ToList();
            
            // Clear extracted text to force reprocessing
            foreach (var docId in filteredIds)
            {
                var doc = await dbContext.JumpDocuments.FindAsync(docId);
                if (doc != null)
                {
                    doc.ExtractedText = null;
                    doc.ExtractionMethod = null;
                }
            }
            
            await dbContext.SaveChangesAsync();

            return Results.Ok(new
            {
                success = true,
                queued = filteredIds.Count,
                message = "Documents queued for reprocessing. Use 'Start Processing' in Text Extraction Status section to begin."
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting reprocessing: {ex.Message}");
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Parse OCR quality from ExtractionMethod field
    /// Expected format: "tesseract_ocr_0.85" or "tesseract_ocr_low_quality_0.35"
    /// </summary>
    private static double? ParseOcrQuality(string? extractionMethod)
    {
        if (string.IsNullOrEmpty(extractionMethod))
            return null;

        // Check if it's a tesseract OCR method
        if (!extractionMethod.Contains("tesseract_ocr"))
            return null;

        // Try to extract the quality number (last part after underscore)
        var parts = extractionMethod.Split('_');
        if (parts.Length == 0)
            return null;

        var lastPart = parts[^1];
        if (double.TryParse(lastPart, out var quality))
        {
            return quality;
        }

        return null;
    }
    
    // Request models
    private record ChangeUsernameRequest(string NewUsername, string CurrentPassword);
    private record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    private record ScanScheduleRequest(bool Enabled, int IntervalHours);
    private record RecategorizeTagRequest(string TagName, string OldCategory, string NewCategory);
    private record ReprocessRequest(int TextThreshold, double QualityThreshold);
}
