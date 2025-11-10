# Quick Test and Start Script for JumpChain Overnight Processing
Write-Host "🚀 JumpChain Quick Start with Authentication Test" -ForegroundColor Cyan

# Start server in background
Write-Host "Starting server..." -ForegroundColor Yellow
$serverProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --urls http://0.0.0.0:5248" -WorkingDirectory "." -PassThru -WindowStyle Hidden

# Wait for server to start
Write-Host "Waiting for server to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# Test server and authentication
Write-Host "Testing server and authentication..." -ForegroundColor Cyan
try {
    $test = Invoke-RestMethod -Uri "http://localhost:5248/simple-test" -Method GET -TimeoutSec 10
    Write-Host "✅ Server is running" -ForegroundColor Green
    
    # Test extraction
    $extract = Invoke-RestMethod -Uri "http://localhost:5248/bulk-extract-text?batchSize=1" -Method POST -TimeoutSec 90
    
    if ($extract.successCount -gt 0) {
        Write-Host "🎉 AUTHENTICATION WORKING! Extraction successful!" -ForegroundColor Green
        Write-Host "🌐 Server accessible at: http://localhost:5248 (network: http://YOUR_IP:5248)" -ForegroundColor Cyan
        Write-Host "`nReady to start overnight processing!" -ForegroundColor Green
        
        # Get status
        $status = Invoke-RestMethod -Uri "http://localhost:5248/debug-extraction-status" -Method GET
        Write-Host "Documents to process: $($status.unprocessed)" -ForegroundColor White
        
        Write-Host "`n🚀 Starting overnight batch processing..." -ForegroundColor Green
        .\overnight-batch-processing-fixed.ps1
        
    } else {
        Write-Host "❌ Authentication still not working - no successful extractions" -ForegroundColor Red
        Write-Host "Server PID: $($serverProcess.Id)" -ForegroundColor Gray
        Write-Host "You may need to check the .env file or restart manually" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "❌ Server test failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Server PID: $($serverProcess.Id)" -ForegroundColor Gray
}
