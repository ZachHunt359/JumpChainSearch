# JumpChain Overnight Batch Processing Script with Improved Error Handling
param(
    [int]$BatchSize = 15,              # Reduced from 25 to avoid server overload
    [int]$DelayBetweenBatches = 45,    # Increased delay for server recovery
    [int]$MaxRetries = 3,              # Retries per batch
    [int]$MaxConsecutiveFailures = 5,  # Allow more failures before stopping
    [int]$TimeoutSeconds = 900,        # 15 minutes for large files
    [string]$LogFile = "logs\batch-processing-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
)

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] $Message"
    Write-Host $logEntry -ForegroundColor $(switch($Level) { "ERROR" {"Red"} "WARN" {"Yellow"} "SUCCESS" {"Green"} default {"White"} })
    Add-Content -Path $LogFile -Value $logEntry
}

function Test-ServerConnection {
    try {
        $result = Invoke-RestMethod -Uri "http://localhost:5248/simple-test" -Method GET -TimeoutSec 10
        return $true
    } catch {
        return $false
    }
}

function Test-Authentication {
    try {
        $result = Invoke-RestMethod -Uri "http://localhost:5248/debug-auth" -Method GET
        return ($result.authentication.hasServiceAccountKey -eq $true -or $result.authentication.hasApiKey -eq $true)
    } catch {
        return $false
    }
}

function Get-ExtractionStatus {
    try {
        $result = Invoke-RestMethod -Uri "http://localhost:5248/debug-extraction-status" -Method GET
        return $result
    } catch {
        Write-Log "Failed to get extraction status: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

# Pre-flight checks
Write-Log "Starting JumpChain overnight batch processing with improved error handling"
Write-Log "Configuration: BatchSize=$BatchSize, DelayBetweenBatches=$DelayBetweenBatches seconds, Timeout=$TimeoutSeconds seconds"
Write-Log "Failure tolerance: Max $MaxConsecutiveFailures consecutive batch failures allowed"

if (-not (Test-ServerConnection)) {
    Write-Log "❌ Server is not responding at http://localhost:5248" "ERROR"
    Write-Log "Please start the server with: dotnet run --urls `"http://0.0.0.0:5248`"" "ERROR"
    exit 1
}

if (-not (Test-Authentication)) {
    Write-Log "❌ Server authentication failed - no valid API credentials" "ERROR"
    Write-Log "Please restart the server to reload .env file: dotnet run --urls `"http://0.0.0.0:5248`"" "ERROR"
    exit 1
}

Write-Log "✅ Server connection and authentication verified" "SUCCESS"

# Get initial status
$initialStatus = Get-ExtractionStatus
if (-not $initialStatus) {
    Write-Log "Cannot get initial status. Aborting." "ERROR"
    exit 1
}

Write-Log "Initial Status: $($initialStatus.total) total, $($initialStatus.actuallyExtracted) extracted, $($initialStatus.unprocessed) unprocessed"

if ($initialStatus.unprocessed -eq 0) {
    Write-Log "No documents need processing. All done!" "SUCCESS"
    exit 0
}

# Main processing variables
$totalProcessed = 0
$totalSuccesses = 0
$totalErrors = 0
$consecutiveFailures = 0
$batchNumber = 1
$startTime = Get-Date

# Main processing loop
while ($true) {
    Write-Log "Starting batch $batchNumber (size: $BatchSize)"
    
    $retryCount = 0
    $batchSuccess = $false
    
    while ($retryCount -lt $MaxRetries -and -not $batchSuccess) {
        try {
            $result = Invoke-RestMethod -Uri "http://localhost:5248/bulk-extract-text?batchSize=$BatchSize" -Method POST -TimeoutSec $TimeoutSeconds
            
            if ($result -and $result.successCount -ne $null) {
                $batchSuccesses = $result.successCount
                $batchErrors = $result.errorCount
                $batchTimeouts = if ($result.timeoutCount) { $result.timeoutCount } else { 0 }
                
                # FIXED: Use actual processed count, not truncated results array
                $batchProcessed = $batchSuccesses + $batchErrors + $batchTimeouts
                
                # Debug logging to catch calculation errors
                Write-Log "DEBUG: API Response - Processed: $batchProcessed, Successes: $batchSuccesses, Errors: $batchErrors, Timeouts: $batchTimeouts" "INFO"
                Write-Log "DEBUG: Results array length: $(if ($result.results) { $result.results.Count } else { 0 }) (truncated for display)" "INFO"
                
                $totalProcessed += $batchProcessed
                $totalSuccesses += $batchSuccesses
                $totalErrors += $batchErrors
                
                $successRate = if ($batchProcessed -gt 0) { [math]::Round(($batchSuccesses / $batchProcessed) * 100, 1) } else { 0 }
                
                # Validate the math
                if ($batchSuccesses -gt $batchProcessed) {
                    Write-Log "WARNING: Success count ($batchSuccesses) exceeds processed count ($batchProcessed) - possible API issue!" "WARN"
                }
                
                Write-Log "Batch $batchNumber completed: $batchProcessed processed, $batchSuccesses successes ($successRate%), $batchErrors errors, $batchTimeouts timeouts" "SUCCESS"
                
                if ($result.remainingDocuments -eq 0 -or $result.hasMoreToProcess -eq $false) {
                    Write-Log "All documents processed! No more remaining." "SUCCESS"
                    break
                }
                
                $batchSuccess = $true
                $consecutiveFailures = 0  # Reset failure counter on success
            } else {
                Write-Log "Batch $batchNumber returned invalid results" "WARN"
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
                $consecutiveFailures++
            }
        }
    }
    
    if (-not $batchSuccess) {
        if ($consecutiveFailures -ge $MaxConsecutiveFailures) {
            Write-Log "Too many consecutive batch failures ($consecutiveFailures). Stopping processing." "ERROR"
            break
        } else {
            Write-Log "Batch failed but continuing... ($consecutiveFailures/$MaxConsecutiveFailures consecutive failures)" "WARN"
            # Continue to next batch even after failure
        }
    }
    
    # Progress report every 10 batches
    if ($batchNumber % 10 -eq 0) {
        $elapsed = (Get-Date) - $startTime
        $rate = if ($elapsed.TotalMinutes -gt 0) { [math]::Round($totalProcessed / $elapsed.TotalMinutes, 1) } else { 0 }
        Write-Log "Progress: $totalProcessed processed, $totalSuccesses successes, $totalErrors errors. Rate: $rate docs/min" "INFO"
        
        $currentStatus = Get-ExtractionStatus
        if ($currentStatus) {
            Write-Log "Current database status: $($currentStatus.actuallyExtracted) extracted, $($currentStatus.unprocessed) remaining" "INFO"
            
            if ($currentStatus.unprocessed -eq 0) {
                Write-Log "Database shows no remaining documents!" "SUCCESS"
                break
            }
        }
    }
    
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
