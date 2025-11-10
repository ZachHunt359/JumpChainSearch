# Binary Search Batch Processing - Divide and Conquer Approach
Write-Host "[BINARY] JumpChain Binary Search Batch Processing Started" -ForegroundColor Green

# Queue to store batch ranges to process: [startId, count]
$batchQueue = @()
$processedCount = 0
$quarantinedDocs = @()
$maxIterations = 1000  # Safety limit

# Initialize with first batch
$initialBatchSize = 50
$batchQueue += @{ StartId = $null; Count = $initialBatchSize; Depth = 0 }

Write-Host "[INIT] Starting with initial batch size: $initialBatchSize" -ForegroundColor Cyan

for ($iteration = 1; $iteration -le $maxIterations -and $batchQueue.Count -gt 0; $iteration++) {
    $currentBatch = $batchQueue[0]
    $batchQueue = $batchQueue[1..($batchQueue.Count-1)]  # Remove first item
    
    $indent = "  " * $currentBatch.Depth
    Write-Host "$indent[BATCH $iteration] Processing batch of $($currentBatch.Count) documents (depth: $($currentBatch.Depth))" -ForegroundColor Yellow
    
    try {
        # Get current status
        $status = Invoke-RestMethod -Uri "http://localhost:5248/batch-status" -Method GET
        Write-Host "$indent[STATUS] $($status.processed)/$($status.total) documents processed overall" -ForegroundColor White
        
        if (-not $status.hasMoreToProcess) {
            Write-Host "$indent[COMPLETE] No more documents to process!" -ForegroundColor Green
            break
        }
        
        # Process the batch
        $response = Invoke-RestMethod -Uri "http://localhost:5248/bulk-extract-text?batchSize=$($currentBatch.Count)" -Method POST -TimeoutSec 300
        
        Write-Host "$indent[RESULT] Success: $($response.successCount), Errors: $($response.errorCount), Timeouts: $($response.timeoutCount)" -ForegroundColor White
        $processedCount += ($response.successCount + $response.errorCount + $response.timeoutCount)
        
        # If batch succeeded (no timeouts), continue with next batch
        if ($response.timeoutCount -eq 0) {
            Write-Host "$indent[SUCCESS] Batch completed successfully" -ForegroundColor Green
            
            # Add another batch of the same size to the queue
            if ($status.hasMoreToProcess) {
                $batchQueue += @{ StartId = $null; Count = $currentBatch.Count; Depth = 0 }
            }
        }
        else {
            Write-Host "$indent[SPLIT] Batch had $($response.timeoutCount) timeouts - splitting" -ForegroundColor Red
            
            # Binary split: divide the batch size in half
            $leftSize = [Math]::Floor($currentBatch.Count / 2)
            $rightSize = $currentBatch.Count - $leftSize
            
            if ($leftSize -ge 1) {
                Write-Host "$indent[QUEUE] Adding left batch: $leftSize documents" -ForegroundColor Magenta
                $batchQueue += @{ StartId = $null; Count = $leftSize; Depth = $currentBatch.Depth + 1 }
            }
            
            if ($rightSize -ge 1) {
                Write-Host "$indent[QUEUE] Adding right batch: $rightSize documents" -ForegroundColor Magenta  
                $batchQueue += @{ StartId = $null; Count = $rightSize; Depth = $currentBatch.Depth + 1 }
            }
            
            # If we'"'"'ve split down to individual documents, quarantine them
            if ($currentBatch.Count -eq 1) {
                Write-Host "$indent[QUARANTINE] Single document caused timeout - adding to quarantine list" -ForegroundColor Red
                $quarantinedDocs += "Batch-$iteration (Size: 1)"
            }
        }
        
        Write-Host "$indent[QUEUE] Remaining batches in queue: $($batchQueue.Count)" -ForegroundColor Gray
        Start-Sleep 2
        
    } catch {
        Write-Host "$indent[ERROR] Batch failed: $($_.Exception.Message)" -ForegroundColor Red
        
        # On HTTP timeout or other errors, also try to split
        if ($currentBatch.Count -gt 1) {
            $leftSize = [Math]::Floor($currentBatch.Count / 2)  
            $rightSize = $currentBatch.Count - $leftSize
            
            Write-Host "$indent[SPLIT-ERROR] Splitting failed batch: $leftSize + $rightSize" -ForegroundColor Red
            
            if ($leftSize -ge 1) {
                $batchQueue += @{ StartId = $null; Count = $leftSize; Depth = $currentBatch.Depth + 1 }
            }
            if ($rightSize -ge 1) {
                $batchQueue += @{ StartId = $null; Count = $rightSize; Depth = $currentBatch.Depth + 1 }
            }
        } else {
            Write-Host "$indent[QUARANTINE-ERROR] Single document failed - quarantining" -ForegroundColor Red
            $quarantinedDocs += "Batch-$iteration (Error: Single doc)"
        }
    }
}

Write-Host "[SUMMARY] Binary search batch processing completed" -ForegroundColor Green
Write-Host "[STATS] Total iterations: $iteration" -ForegroundColor White
Write-Host "[STATS] Documents processed this session: $processedCount" -ForegroundColor White
Write-Host "[STATS] Quarantined problematic batches: $($quarantinedDocs.Count)" -ForegroundColor White

if ($quarantinedDocs.Count -gt 0) {
    Write-Host "[QUARANTINE] Problematic batches:" -ForegroundColor Yellow
    foreach ($doc in $quarantinedDocs) {
        Write-Host "  - $doc" -ForegroundColor Red
    }
}

Write-Host "[DONE] Binary search session completed" -ForegroundColor Green
