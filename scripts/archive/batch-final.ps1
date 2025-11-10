# JumpChain Batch Processing - Final Version with Fixed Database Logic
Write-Host "[BATCH] JumpChain Bulk Text Extraction Started" -ForegroundColor Green

$batchCount = 0
$totalProcessed = 0
$totalErrors = 0
$totalTimeouts = 0

while ($true) {
    try {
        $batchCount++
        
        # Get current status
        $status = Invoke-RestMethod -Uri "http://localhost:5248/batch-status" -Method GET
        Write-Host "[BATCH $batchCount] $($status.processed)/$($status.total) documents ($([math]::Round($status.percentComplete, 1))% complete)" -ForegroundColor Yellow
        
        # Check if processing is complete
        if (-not $status.hasMoreToProcess) {
            Write-Host "[COMPLETE] All documents have been processed!" -ForegroundColor Green
            break
        }
        
        Write-Host "[WORK] Processing batch $batchCount (timeout: 5 minutes)..." -ForegroundColor Cyan
        
        # Process batch with 50 documents
        $response = Invoke-RestMethod -Uri "http://localhost:5248/bulk-extract-text?batchSize=50" -Method POST -TimeoutSec 300
        
        $totalProcessed += ($response.successCount + $response.errorCount + $response.timeoutCount)
        $totalErrors += $response.errorCount
        $totalTimeouts += $response.timeoutCount
        
        Write-Host "[RESULT] Success: $($response.successCount), Errors: $($response.errorCount), Timeouts: $($response.timeoutCount)" -ForegroundColor White
        Write-Host "[PROGRESS] Session totals - Processed: $totalProcessed, Errors: $totalErrors, Timeouts: $totalTimeouts" -ForegroundColor Magenta
        
        # Adaptive delay based on error rate
        if ($response.errorCount -gt 40) {
            Write-Host "[DELAY] High error rate, waiting 60 seconds..." -ForegroundColor Red
            Start-Sleep 60
        } elseif ($response.errorCount -gt 20) {
            Write-Host "[DELAY] Moderate errors, waiting 30 seconds..." -ForegroundColor Yellow
            Start-Sleep 30
        } else {
            Write-Host "[DELAY] Normal processing, waiting 10 seconds..." -ForegroundColor Green
            Start-Sleep 10
        }
        
    } catch {
        Write-Host "[ERROR] Batch $batchCount failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "[RETRY] Waiting 60 seconds before retry..." -ForegroundColor Yellow
        Start-Sleep 60
    }
}

Write-Host "[DONE] Batch processing session completed. Total processed: $totalProcessed" -ForegroundColor Green
