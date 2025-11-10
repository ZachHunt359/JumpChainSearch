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
        }}
        
        .header {{
            background: var(--bg-secondary);
            border-bottom: 2px solid var(--accent);
            padding: 1.5rem 2rem;
            display: flex;
            justify-content: space-between;
            align-items: center;
            box-shadow: 0 2px 10px rgba(0,0,0,0.3);
        }}
        
        .header h1 {{
            font-size: 1.8rem;
            color: var(--text-primary);
        }}
        
        .header-info {{
            display: flex;
            align-items: center;
            gap: 1.5rem;
        }}
        
        .user-info {{
            color: var(--text-secondary);
            font-size: 0.9rem;
        }}
        
        .btn {{
            padding: 0.5rem 1.5rem;
            background: var(--accent);
            color: white;
            border: none;
            border-radius: 5px;
            cursor: pointer;
            font-size: 0.9rem;
            transition: background 0.3s, transform 0.1s;
            text-decoration: none;
            display: inline-block;
        }}
        
        .btn:hover {{
            background: var(--accent-hover);
            transform: translateY(-2px);
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
        
        .container {{
            max-width: 1400px;
            margin: 0 auto;
            padding: 2rem;
        }}
        
        .stats-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
            gap: 1.5rem;
            margin-bottom: 2rem;
        }}
        
        .stat-card {{
            background: var(--bg-secondary);
            border: 1px solid var(--border);
            border-radius: 10px;
            padding: 1.5rem;
            box-shadow: 0 4px 6px rgba(0,0,0,0.2);
        }}
        
        .stat-card h3 {{
            color: var(--text-secondary);
            font-size: 0.9rem;
            margin-bottom: 0.5rem;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }}
        
        .stat-value {{
            font-size: 2.5rem;
            font-weight: bold;
            color: var(--accent);
        }}
        
        .section {{
            background: var(--bg-secondary);
            border: 1px solid var(--border);
            border-radius: 10px;
            padding: 2rem;
            margin-bottom: 2rem;
            box-shadow: 0 4px 6px rgba(0,0,0,0.2);
        }}
        
        .section h2 {{
            color: var(--text-primary);
            margin-bottom: 1.5rem;
            font-size: 1.5rem;
            border-bottom: 2px solid var(--accent);
            padding-bottom: 0.5rem;
        }}
        
        .action-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
            gap: 1.5rem;
        }}
        
        .action-card {{
            background: var(--bg-tertiary);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 1.5rem;
        }}
        
        .action-card h3 {{
            color: var(--text-primary);
            margin-bottom: 1rem;
            font-size: 1.2rem;
        }}
        
        .action-card p {{
            color: var(--text-secondary);
            font-size: 0.9rem;
            margin-bottom: 1rem;
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
    <div class=""header"">
        <h1>🚀 JumpChain Admin Portal</h1>
        <div class=""header-info"">
            <span class=""user-info"">Logged in as: <strong>{username}</strong></span>
            <a href=""/Admin/Logout"" class=""btn btn-secondary"">Logout</a>
        </div>
    </div>
    
    <div class=""container"">
        <!-- Stats Dashboard -->
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
        
        <!-- Batch Processing Section -->
        <div class=""section"">
            <h2>📦 Batch Processing</h2>
            <div class=""action-grid"">
                <div class=""action-card"">
                    <h3>Text Extraction</h3>
                    <p>Process documents to extract text content for search indexing.</p>
                    <span class=""status status-idle"" id=""batch-status"">Checking...</span>
                    <div class=""btn-group"" style=""margin-top: 1rem;"">
                        <button class=""btn btn-success"" onclick=""startBatch()"">Start Processing</button>
                        <button class=""btn btn-danger"" onclick=""stopBatch()"">Stop</button>
                    </div>
                    <div id=""batch-info"" style=""margin-top: 1rem; color: var(--text-secondary); font-size: 0.85rem;""></div>
                </div>
            </div>
        </div>
        
        <!-- Drive Scanning Section -->
        <div class=""section"">
            <h2>💾 Google Drive Scanning</h2>
            <div class=""action-grid"">
                <div class=""action-card"">
                    <h3>Drive Sync</h3>
                    <p>Scan configured Google Drives for new JumpChain documents.</p>
                    <span class=""status status-idle"" id=""drive-status"">Checking...</span>
                    <div class=""btn-group"" style=""margin-top: 1rem;"">
                        <button class=""btn btn-success"" onclick=""startDriveScan()"">Start Scan</button>
                        <button class=""btn btn-danger"" onclick=""stopDriveScan()"">Stop</button>
                    </div>
                    <div id=""drive-info"" style=""margin-top: 1rem; color: var(--text-secondary); font-size: 0.85rem;""></div>
                </div>
            </div>
        </div>
        
        <!-- Genre Tagging Section -->
        <div class=""section"">
            <h2>🏷️ Genre Tagging</h2>
            <div class=""action-grid"">
                <div class=""action-card"">
                    <h3>Community Genre Tags</h3>
                    <p>Apply genre tags from the community tag list to matching documents.</p>
                    <button class=""btn btn-success"" onclick=""applyGenreTags()"">Apply Genre Tags</button>
                    <div id=""genre-info"" style=""margin-top: 1rem; color: var(--text-secondary); font-size: 0.85rem;""></div>
                </div>
            </div>
        </div>
        
        <!-- System Management Section -->
        <div class=""section"">
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
                    <div id=""logs"" class=""log-container"" style=""display: none;""></div>
                </div>
            </div>
        </div>
    </div>
    
    <script>
        // Auto-refresh status every 5 seconds
        setInterval(updateStatus, 5000);
        updateStatus();
        
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
                alert(data.message || (data.success ? 'Batch processing started!' : 'Failed to start batch processing'));
                updateStatus();
            }} catch (e) {{
                alert('Error: ' + e.message);
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
                alert(data.message || (data.success ? 'Drive scan started!' : 'Failed to start scan'));
                updateStatus();
            }} catch (e) {{
                alert('Error: ' + e.message);
            }}
        }}
        
        async function stopDriveScan() {{
            if (!confirm('Stop drive scanning?')) return;
            try {{
                const resp = await fetch('/admin/drives/scan/stop', {{ method: 'POST' }});
                const data = await resp.json();
                alert(data.message || 'Drive scan stopped');
                updateStatus();
            }} catch (e) {{
                alert('Error: ' + e.message);
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
            var restartScript = @"
Start-Sleep -Seconds 2
Get-Process -Name 'dotnet' -ErrorAction SilentlyContinue | Where-Object { $_.MainModule.FileName -like '*JumpChainSearch*' } | Stop-Process -Force
Start-Sleep -Seconds 3
Set-Location '" + Directory.GetCurrentDirectory() + @"'
Start-Process -FilePath 'dotnet' -ArgumentList 'run --urls http://0.0.0.0:5248' -WindowStyle Hidden
";
            
            File.WriteAllText("restart-server.ps1", restartScript);
            
            // Execute restart script in background
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-ExecutionPolicy Bypass -File restart-server.ps1",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
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

            // Create PowerShell script to scan drives
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

            var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "drive-scan.ps1");
            File.WriteAllText(scriptPath, scanScript);

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

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
