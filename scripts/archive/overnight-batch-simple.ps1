# JumpChain Overnight Batch Processing Script - Simple Version
Write-Host '[MOON] JumpChain Overnight Batch Processing Started' -ForegroundColor Cyan

$serverUrl = 'http://localhost:5248'
$maxErrors = 3
$errorCount = 0
$batchCount = 0

while ($true) {
    try {
        # Check status
        $status = Invoke-RestMethod -Uri "$serverUrl/batch-status" -Method GET -TimeoutSec 10
        $processed = $status.processed
        $total = $status.total
        $percent = [math]::Round(($processed / $total) * 100, 1)
        
        Write-Host "[BATCH $batchCount] $processed/$total documents ($percent% complete)" -ForegroundColor Green
        
        if ($processed -ge $total) {
            Write-Host '[COMPLETE] All documents processed!' -ForegroundColor Green
            break
        }
        
        # Process batch
        Write-Host "[WORK] Processing batch $batchCount..." -ForegroundColor Blue
        $result = Invoke-RestMethod -Uri "$serverUrl/bulk-extract-text" -Method POST -TimeoutSec 300
        
        Write-Host "[SUCCESS] $($result.message)" -ForegroundColor Green
        $errorCount = 0
        $batchCount++
        Start-Sleep -Seconds 5
        
    } catch {
        $errorCount++
        $errorMsg = $_.Exception.Message
        Write-Host "[ERROR] $errorMsg" -ForegroundColor Red
        
        if ($errorMsg -like '*timeout*') {
            Write-Host '[TIMEOUT] Waiting 2 minutes...' -ForegroundColor Yellow
            Start-Sleep -Seconds 120
            $errorCount = 0
        } elseif ($errorCount -ge $maxErrors) {
            Write-Host '[FATAL] Too many errors. Exiting.' -ForegroundColor Red
            break
        } else {
            Start-Sleep -Seconds 30
        }
    }
}
