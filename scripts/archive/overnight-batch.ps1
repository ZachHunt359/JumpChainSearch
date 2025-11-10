# JumpChain Overnight Batch Processing Script
# Usage: .\overnight-batch.ps1

param(
    [int]$DelayMs = 18000,
    [int]$MaxConsecutiveErrors = 5,
    [string]$ServerUrl = "http://localhost:5248"
)

Write-Host "[MOON] JumpChain Overnight Batch Processing Started" -ForegroundColor Cyan
Write-Host "[INFO] Configuration:" -ForegroundColor Gray
Write-Host "   Batch Size: 50 documents per call" -ForegroundColor Gray  
$batchesPerMin = [math]::Round(60000 / $DelayMs, 1)
Write-Host "   Delay: $DelayMs ms between batches ($batchesPerMin batches/min)" -ForegroundColor Gray
Write-Host "   Max Errors: $MaxConsecutiveErrors consecutive failures" -ForegroundColor Gray
Write-Host "   Server: $ServerUrl" -ForegroundColor Gray
Write-Host ""

$startTime = Get-Date
$totalProcessed = 0  
$consecutiveErrors = 0
$lastCheckpoint = Get-Date
$batchCount = 0

# Main processing loop
while ($true) {
    try {
        # Check current status
        $status = Invoke-RestMethod -Uri "$ServerUrl/batch-status" -Method GET -TimeoutSec 10
        $percent = [math]::Round($status.percentComplete, 1)
        
        Write-Host "[PROGRESS] $($status.processed)/$($status.total) documents ($percent% complete)" -ForegroundColor Green
        
        if ($status.unprocessed -eq 0) {
            $totalTime = (Get-Date) - $startTime
            Write-Host "[SUCCESS] ALL DOCUMENTS PROCESSED!" -ForegroundColor Green
            $totalHours = [math]::Round($totalTime.TotalHours, 1)
            Write-Host "[STATS] Final: $totalProcessed processed in $totalHours hours across $batchCount batches" -ForegroundColor Green
            break
        }
        
        Write-Host "[WORK] Calling bulk-extract-text endpoint..." -ForegroundColor Yellow
        
        # Use the existing bulk-extract-text endpoint
        $result = Invoke-RestMethod -Uri "$ServerUrl/bulk-extract-text" -Method POST -TimeoutSec 300
        $batchCount++
        
        if ($result.success) {
            $totalProcessed += $result.processedCount
            Write-Host "[BATCH] #$batchCount completed: $($result.processedCount) documents processed" -ForegroundColor White
            
            # Reset error counter on success
            $consecutiveErrors = 0
            
            # If no documents were processed, we're done
            if ($result.processedCount -eq 0) {
                Write-Host "[DONE] No more documents to process" -ForegroundColor Green
                break
            }
        } else {
            Write-Host "[FAIL] Batch #$batchCount failed: $($result.error)" -ForegroundColor Red
            
            # Check for rate limiting
            if ($result.error -like "*rate*" -or $result.error -like "*quota*" -or $result.error -like "*429*" -or $result.error -like "*limit*") {
                throw "Rate limit detected: $($result.error)"
            }
        }
        
        # Progress update every 10 batches
        if ($batchCount % 10 -eq 0) {
            $elapsed = (Get-Date) - $startTime
            $rate = if ($elapsed.TotalHours -gt 0) { $totalProcessed / $elapsed.TotalHours } else { 0 }
            $elapsedHours = [math]::Round($elapsed.TotalHours, 2)
            $rateRounded = [math]::Round($rate, 1)
            Write-Host "[UPDATE] $batchCount batches completed in $elapsedHours h (Rate: $rateRounded docs/hr)" -ForegroundColor Cyan
        }
        
        # Rate limiting delay between batches (Google API rate limits)
        Write-Host "[SLEEP] Waiting $([math]::Round($DelayMs/1000, 1))s before next batch..." -ForegroundColor Gray
        Start-Sleep -Milliseconds $DelayMs
    }
    catch {
        $consecutiveErrors++
        $pauseMinutes = [Math]::Min(15 + ($consecutiveErrors - 1) * 5, 60)
        
        Write-Host "[ERROR] BATCH FAILED (Error #$consecutiveErrors): $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "[WAIT] Pausing for $pauseMinutes minutes before retry..." -ForegroundColor Yellow
        
        if ($consecutiveErrors -ge $MaxConsecutiveErrors) {
            Write-Host "[STOP] TOO MANY CONSECUTIVE ERRORS ($consecutiveErrors). STOPPING OVERNIGHT PROCESSING." -ForegroundColor Red
            break
        }
        
        # Wait before retrying
        Start-Sleep -Seconds ($pauseMinutes * 60)
    }
    
    # 12-hour checkpoint for long runs
    if (((Get-Date) - $lastCheckpoint).TotalHours -ge 12) {
        Write-Host "[CHECKPOINT] 12-HOUR CHECKPOINT: Taking 30-minute break..." -ForegroundColor Cyan
        $elapsed = (Get-Date) - $startTime
        $elapsedHours = [math]::Round($elapsed.TotalHours, 1)
        Write-Host "[CHECKPOINT] Status: $totalProcessed docs processed in $elapsedHours hours" -ForegroundColor Cyan
        Start-Sleep -Seconds (30 * 60)
        $lastCheckpoint = Get-Date
    }
}

# Final summary
$totalTime = (Get-Date) - $startTime
Write-Host ""
Write-Host "[COMPLETE] OVERNIGHT BATCH PROCESSING COMPLETED!" -ForegroundColor Green
Write-Host "[STATS] Total processed: $totalProcessed documents" -ForegroundColor Green  
Write-Host "[BATCHES] Total batches: $batchCount" -ForegroundColor Green
$finalHours = [math]::Round($totalTime.TotalHours, 1)
Write-Host "[TIME] Total time: $finalHours hours" -ForegroundColor Green
if ($totalTime.TotalHours -gt 0) {
    $finalRate = [math]::Round($totalProcessed / $totalTime.TotalHours, 1)
    Write-Host "[RATE] Average rate: $finalRate documents/hour" -ForegroundColor Green
}
Write-Host "[SLEEP] Ready for sleep!" -ForegroundColor Cyan