# JumpChain Overnight Batch Processing Script
# Created: $(Get-Date)
# Purpose: Process all remaining documents using improved extraction methods

param(
    [int]$BatchSize = 25,           # Conservative batch size to avoid rate limits
    [int]$DelayBetweenBatches = 30, # Seconds between batches
    [int]$MaxRetries = 3,           # Retry failed batches
    [string]$LogFile = "batch-processing-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
)

# Function to write timestamped log entries
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] $Message"
    Write-Host $logEntry -ForegroundColor $(switch($Level) { "ERROR" {"Red"} "WARN" {"Yellow"} "SUCCESS" {"Green"} default {"White"} })
    Add-Content -Path $LogFile -Value $logEntry
}

# Function to check if server is running
function Test-ServerConnection {
    try {
        $result = Invoke-RestMethod -Uri "http://localhost:5248/simple-test" -Method GET -TimeoutSec 10
        return $true
    } catch {
        return $false
    }
}

# Function to get current extraction status
function Get-ExtractionStatus {
    try {
        $result = Invoke-RestMethod -Uri "http://localhost:5248/debug-extraction-status" -Method GET
        return $result
    } catch {
        Write-Log "Failed to get extraction status: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

# Main processing function
function Start-BatchProcessing {
    Write-Log "Starting JumpChain overnight batch processing"
    Write-Log "Configuration: BatchSize=$BatchSize, DelayBetweenBatches=$DelayBetweenBatches seconds"
    
    # Check server connection
    if (-not (Test-ServerConnection)) {
        Write-Log "Server is not responding at http://localhost:5248. Please start the server first." "ERROR"
        return
    }
    
    # Get initial status
    $initialStatus = Get-ExtractionStatus
    if (-not $initialStatus) {
        Write-Log "Cannot get initial status. Aborting." "ERROR"
        return
    }
    
    Write-Log "Initial Status: $($initialStatus.total) total, $($initialStatus.actuallyExtracted) extracted, $($initialStatus.unprocessed) unprocessed"
    
    if ($initialStatus.unprocessed -eq 0) {
        Write-Log "No documents need processing. All done!" "SUCCESS"
        return
    }
    
    $totalProcessed = 0
    $totalSuccesses = 0
    $totalErrors = 0
    $batchNumber = 1
    $startTime = Get-Date
    
    # Main processing loop
    while ($true) {
        Write-Log "Starting batch $batchNumber (size: $BatchSize)"
        
        $retryCount = 0
        $batchSuccess = $false
        
        # Retry logic for each batch
        while ($retryCount -lt $MaxRetries -and -not $batchSuccess) {
            try {
                $result = Invoke-RestMethod -Uri "http://localhost:5248/bulk-extract-text?batchSize=$BatchSize" -Method POST -TimeoutSec 300
                
                if ($result -and $result.success) {
                    # Calculate total processed from API response (successCount + errorCount + timeoutCount)
                    $batchProcessed = ($result.successCount + $result.errorCount + $result.timeoutCount)
                    $batchSuccesses = $result.successCount
                    $batchErrors = $result.errorCount + $result.timeoutCount
                    
                    $totalProcessed += $batchProcessed
                    $totalSuccesses += $batchSuccesses
                    $totalErrors += $batchErrors
                    
                    $successRate = if ($batchProcessed -gt 0) { [math]::Round(($batchSuccesses / $batchProcessed) * 100, 1) } else { 0 }
                    
                    Write-Log "Batch $batchNumber completed: $batchProcessed processed, $batchSuccesses successes ($successRate%), $batchErrors errors" "SUCCESS"
                    
                    if ($result.remainingDocuments -eq 0 -or -not $result.hasMoreToProcess) {
                        Write-Log "All documents processed! No more remaining." "SUCCESS"
                        break
                    }
                    
                    $batchSuccess = $true
                } else {
                    Write-Log "Batch $batchNumber failed or returned no results. Result: $($result | ConvertTo-Json -Compress)" "WARN"
                    break
                }
            }
            catch {
                $retryCount++
                Write-Log "Batch $batchNumber attempt $retryCount failed: $($_.Exception.Message)" "ERROR"
                
                if ($retryCount -lt $MaxRetries) {
                    Write-Log "Retrying batch $batchNumber in 60 seconds..." "WARN"
                    Start-Sleep -Seconds 60
                } else {
                    Write-Log "Batch $batchNumber failed after $MaxRetries attempts" "ERROR"
                    $totalErrors++
                }
            }
        }
        
        # Check if we should continue
        if (-not $batchSuccess) {
            Write-Log "Too many batch failures. Stopping processing." "ERROR"
            break
        }
        
        # Progress report every 10 batches
        if ($batchNumber % 10 -eq 0) {
            $elapsed = (Get-Date) - $startTime
            $rate = if ($elapsed.TotalMinutes -gt 0) { [math]::Round($totalProcessed / $elapsed.TotalMinutes, 1) } else { 0 }
            Write-Log "Progress: $totalProcessed processed, $totalSuccesses successes, $totalErrors errors. Rate: $rate docs/min" "INFO"
            
            # Get updated status
            $currentStatus = Get-ExtractionStatus
            if ($currentStatus) {
                Write-Log "Current database status: $($currentStatus.actuallyExtracted) extracted, $($currentStatus.unprocessed) remaining" "INFO"
                
                if ($currentStatus.unprocessed -eq 0) {
                    Write-Log "Database shows no remaining documents!" "SUCCESS"
                    break
                }
            }
        }
        
        # Wait between batches to respect rate limits
        Write-Log "Waiting $DelayBetweenBatches seconds before next batch..."
        Start-Sleep -Seconds $DelayBetweenBatches
        
        $batchNumber++
    }
    
    # Final report
    $endTime = Get-Date
    $totalElapsed = $endTime - $startTime
    $finalStatus = Get-ExtractionStatus
    
    Write-Log "=== BATCH PROCESSING COMPLETED ===" "SUCCESS"
    Write-Log "Total time: $($totalElapsed.ToString())" "INFO"
    Write-Log "Total batches: $($batchNumber - 1)" "INFO"
    Write-Log "Total documents processed: $totalProcessed" "INFO"
    Write-Log "Total successes: $totalSuccesses" "INFO"
    Write-Log "Total errors: $totalErrors" "INFO"
    
    if ($finalStatus) {
        Write-Log "Final status: $($finalStatus.actuallyExtracted) extracted, $($finalStatus.unprocessed) remaining" "INFO"
        $overallSuccess = if ($totalProcessed -gt 0) { [math]::Round(($totalSuccesses / $totalProcessed) * 100, 1) } else { 0 }
        Write-Log "Overall success rate: $overallSuccess%" "SUCCESS"
    }
    
    Write-Log "Log file: $LogFile" "INFO"
}

# Start the processing
Start-BatchProcessing
