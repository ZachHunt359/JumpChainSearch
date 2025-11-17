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
        
        // Genre tagging endpoints
        group.MapPost("/tags/apply-community-genres", ApplyCommunityGenreTags);
        
        // Series tagging endpoints
        group.MapPost("/tags/apply-community-series", ApplyCommunitySeriesTags);
        
        // Text review queue endpoint
        group.MapGet("/text-review/queue", GetTextReviewQueue);

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
        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Admin Portal - JumpChain Search</title>
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
            <span class=""user-info"">Logged in as: <strong>{username}</strong></span>
            <a href=""/Admin/Logout"" class=""btn btn-secondary"">Logout</a>
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
        </div>
        
        <!-- Drive Scanning Tab -->
        <div id=""drives"" class=""tab-content"">
            <section>
                <h2>💾 Google Drive Scanning</h2>
                <div class=""action-card"">
                    <h3>Drive Sync Status</h3>
                    <p>Scan configured Google Drives for new JumpChain documents.</p>
                    <span class=""status status-idle"" id=""drive-status"">Checking...</span>
                    <div class=""btn-group"" style=""margin-top: 1rem;"">
                        <button class=""btn btn-success"" onclick=""startDriveScan()"">Start Scan</button>
                        <button class=""btn btn-danger"" onclick=""stopDriveScan()"">Stop</button>
                    </div>
                    <div id=""drive-info"" style=""margin-top: 1rem; color: var(--text-secondary); font-size: 0.85rem;""></div>
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
        </div>
    </main>
    
    <script>
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
                        summary.textContent = `${{count}} document${{count !== 1 ? 's' : ''}} need${{count === 1 ? 's' : ''}} review`;
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
                        summary.textContent = `${{count}} pending tag action${{count !== 1 ? 's' : ''}}`;
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
                    batchInfo.innerHTML = `Currently processing: ${{batchData.currentBatch}}<br>Last run: ${{batchData.lastRun}}`;
                }} else {{
                    batchStatus.className = 'status status-idle';
                    batchStatus.textContent = 'Idle';
                    batchInfo.innerHTML = `Last run: ${{batchData.lastRun}}`;
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
                    `✓ Applied ${{data.newTagsApplied}} tags to ${{data.matchedDocuments}} documents`;
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
                    `✓ Applied ${{data.newTagsApplied}} tags to ${{data.matchedDocuments}} documents`;
                alert(data.message || 'Series tags applied successfully!');
            }} catch (e) {{
                document.getElementById('series-info').innerHTML = '✗ Error applying tags';
                alert('Error: ' + e.message);
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
                container.innerHTML = `<div style=""color: var(--danger);"">Error loading queue: ${{error.message}}</div>`;
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
                                    <th style=""padding: 0.5rem;"">Tag</th>
                                    <th style=""padding: 0.5rem;"">Votes</th>
                                    <th style=""padding: 0.5rem;"">Actions</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${{suggestions.map(s => `
                                    <tr style=""border-bottom: 1px solid var(--border);"">
                                        <td style=""padding: 0.5rem;"">${{escapeHtml(s.documentName || 'Doc #' + s.jumpDocumentId)}}</td>
                                        <td style=""padding: 0.5rem;""><strong>${{escapeHtml(s.tagName)}}</strong></td>
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
                                    <th style=""padding: 0.5rem;"">Tag</th>
                                    <th style=""padding: 0.5rem;"">Votes</th>
                                    <th style=""padding: 0.5rem;"">Actions</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${{removals.map(r => `
                                    <tr style=""border-bottom: 1px solid var(--border);"">
                                        <td style=""padding: 0.5rem;"">${{escapeHtml(r.documentName || 'Doc #' + r.jumpDocumentId)}}</td>
                                        <td style=""padding: 0.5rem;""><strong>${{escapeHtml(r.tagName)}}</strong></td>
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
                const response = await fetch(`/api/voting/admin/approve-suggestion/${{id}}`, {{ method: 'POST' }});
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
                const response = await fetch(`/api/voting/admin/reject-suggestion/${{id}}`, {{ method: 'POST' }});
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
                const response = await fetch(`/api/voting/admin/approve-removal/${{id}}`, {{ method: 'POST' }});
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
                const response = await fetch(`/api/voting/admin/reject-removal/${{id}}`, {{ method: 'POST' }});
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

    private static async Task<IResult> StartDriveScan(HttpContext context, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
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
                var scanScript = @"#!/bin/bash
cd " + Directory.GetCurrentDirectory() + @"

timestamp=$(date +'%Y-%m-%d_%H-%M-%S')
logFile=""logs/drive-scan-$timestamp.log""

echo ""Starting drive scan at $(date)"" >> ""$logFile""

curl -X POST http://localhost:5248/api/google-drive/scan-all \
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
}
