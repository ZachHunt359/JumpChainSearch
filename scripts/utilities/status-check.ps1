# Quick Status Check
$status = Invoke-RestMethod -Uri 'http://localhost:5248/batch-status' -Method GET -TimeoutSec 10
$processed = $status.processed
$total = $status.total
$percent = [math]::Round(($processed / $total) * 100, 2)
$remaining = $total - $processed

Write-Host "=== JumpChain Processing Status ===" -ForegroundColor Cyan
Write-Host "Processed: $processed / $total documents" -ForegroundColor Green
Write-Host "Progress: $percent% complete" -ForegroundColor Yellow
Write-Host "Remaining: $remaining documents" -ForegroundColor White
Write-Host "Last Updated: $($status.lastUpdate)" -ForegroundColor Gray
