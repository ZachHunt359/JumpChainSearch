# Enhanced batch processing with better error handling and longer timeouts
param(
    [int]$BatchSize = 50,
    [int]$MaxErrors = 5,
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = "Continue"
$batchCount = 0
$totalErrors = 0

Write-Host "[ROCKET] JumpChain Batch Processing Started (Fixed Version)" -ForegroundColor Cyan

while ($true) {
    try {
        # Get current status
        $status = Invoke-RestMethod -Uri "http://localhost:5248/batch-status" -Method GET -TimeoutSec 30
        $processed = $status.processed
        $total = $status.total
        $percentComplete = [math]::Round(($processed / $total) * 100, 1)
        
        Write-Host "[BATCH $batchCount] $processed/$total documents ($percentComplete% complete)" -ForegroundColor Yellow
        
        if (-not $status.hasMoreToProcess) {
            Write-Host "[DONE] All documents have been processed!" -ForegroundColor Green
            break
        }
        
        Write-Host "[WORK] Processing batch $batchCount (timeout: $TimeoutMinutes minutes)..." -ForegroundColor White
        
        # Process batch with timeout
        $response = Invoke-RestMethod -Uri "http://localhost:5248/bulk-extract-text?batchSize=$BatchSize" -Method POST -TimeoutSec ($TimeoutMinutes * 60)
        
        if ($response.success) {
            $successMsg = "Batch $batchCount - $($response.successCount) success, $($response.errorCount) errors, $($response.timeoutCount) timeouts"
            Write-Host "[SUCCESS] $successMsg" -ForegroundColor Green
            
            # Reset error counter only if we had some actual progress
            if ($response.successCount -gt 0 -or $response.errorCount -gt 0 -or $response.timeoutCount -gt 0) {
                $totalErrors = 0
            }
            
            $batchCount++
            
            # Adaptive delay based on results
            if ($response.errorCount -gt ($response.successCount + $response.timeoutCount)) {
                Write-Host "[CAUTION] Many errors detected, longer delay..." -ForegroundColor Yellow
                Start-Sleep -Seconds 10
            } else {
                Start-Sleep -Seconds 2
            }
        } else {
            throw "Batch processing returned success=false: $($response.error)"
        }
    }
    catch {
        $totalErrors++
        $errorMsg = $_.Exception.Message
        Write-Host "[ERROR $totalErrors/$MaxErrors] $errorMsg" -ForegroundColor Red
        
        if ($errorMsg -like "*timed out*" -or $errorMsg -like "*timeout*") {
            Write-Host "[RETRY] Waiting 45 seconds before retry..." -ForegroundColor Yellow
            Start-Sleep -Seconds 45
        } elseif ($errorMsg -like "*rate limit*" -or $errorMsg -like "*quota*") {
            Write-Host "[RATE LIMIT] Waiting 3 minutes before retry..." -ForegroundColor Yellow
            Start-Sleep -Seconds 180
        } else {
            Write-Host "[RETRY] Waiting $($totalErrors * 30) seconds before retry..." -ForegroundColor Yellow
            Start-Sleep -Seconds ($totalErrors * 30)
        }
        
        if ($totalErrors -ge $MaxErrors) {
            Write-Host "[FATAL] Too many consecutive errors. Exiting." -ForegroundColor Red
            break
        }
    }
}

Write-Host "[DONE] Batch processing session completed" -ForegroundColor Cyan
