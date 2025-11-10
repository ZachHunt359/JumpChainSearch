# Diagnostic Batch Processing - Small batches to find problematic documents
Write-Host "[DIAG] Starting diagnostic batch processing with size 5" -ForegroundColor Green

for ($i = 1; $i -le 20; $i++) {
    try {
        $status = Invoke-RestMethod -Uri "http://localhost:5248/batch-status" -Method GET
        Write-Host "[BATCH $i] $($status.processed)/$($status.total) documents processed" -ForegroundColor Yellow
        
        if (-not $status.hasMoreToProcess) {
            Write-Host "[COMPLETE] No more documents to process" -ForegroundColor Green
            break
        }
        
        Write-Host "[WORK] Processing batch $i with 5 documents (timeout: 2 minutes)..." -ForegroundColor Cyan
        
        $response = Invoke-RestMethod -Uri "http://localhost:5248/bulk-extract-text?batchSize=5" -Method POST -TimeoutSec 120
        
        Write-Host "[RESULT] Success: $($response.successCount), Errors: $($response.errorCount), Timeouts: $($response.timeoutCount)" -ForegroundColor White
        
        # Show which documents were processed
        foreach ($result in $response.results) {
            Write-Host "  Doc $($result.documentId): $($result.name) - $($result.status)" -ForegroundColor Gray
        }
        
        Start-Sleep 5
        
    } catch {
        Write-Host "[ERROR] Batch $i failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "[STOP] Stopping diagnostic - found problematic batch" -ForegroundColor Red
        break
    }
}

Write-Host "[DONE] Diagnostic completed" -ForegroundColor Green
