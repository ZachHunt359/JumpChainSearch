# Quick test script for batch processing
Write-Host "🧪 Testing batch processing calculations..." -ForegroundColor Cyan

try {
    # Run one small batch
    $result = Invoke-RestMethod -Uri "http://localhost:5248/bulk-extract-text?batchSize=5" -Method POST -TimeoutSec 120
    
    $batchProcessed = if ($result.results) { $result.results.Count } else { 0 }
    $batchSuccesses = $result.successCount
    $batchErrors = $result.errorCount
    $batchTimeouts = if ($result.timeoutCount) { $result.timeoutCount } else { 0 }
    
    Write-Host "📊 API Response Analysis:" -ForegroundColor Yellow
    Write-Host "- Documents Processed (results.Count): $batchProcessed" -ForegroundColor White
    Write-Host "- Success Count: $batchSuccesses" -ForegroundColor White  
    Write-Host "- Error Count: $batchErrors" -ForegroundColor White
    Write-Host "- Timeout Count: $batchTimeouts" -ForegroundColor White
    Write-Host "- Remaining Documents: $($result.remainingDocuments)" -ForegroundColor White
    
    # Calculate success rate
    $successRate = if ($batchProcessed -gt 0) { [math]::Round(($batchSuccesses / $batchProcessed) * 100, 1) } else { 0 }
    
    Write-Host ""
    Write-Host "🧮 Calculation Check:" -ForegroundColor Cyan
    Write-Host "- Success Rate: $batchSuccesses ÷ $batchProcessed × 100 = $successRate%" -ForegroundColor White
    
    # Validation
    $total = $batchSuccesses + $batchErrors + $batchTimeouts
    Write-Host "- Total Outcomes: $batchSuccesses + $batchErrors + $batchTimeouts = $total" -ForegroundColor White
    
    if ($total -eq $batchProcessed) {
        Write-Host "✅ Math checks out!" -ForegroundColor Green
    } else {
        Write-Host "❌ Math error: Total outcomes ($total) ≠ Processed ($batchProcessed)" -ForegroundColor Red
    }
    
    if ($batchSuccesses -gt $batchProcessed) {
        Write-Host "❌ IMPOSSIBLE: Successes ($batchSuccesses) > Processed ($batchProcessed)" -ForegroundColor Red
    }
    
} catch {
    Write-Host "❌ Test failed: $($_.Exception.Message)" -ForegroundColor Red
}
