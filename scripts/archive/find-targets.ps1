$targetNames = @("Thunderbolt Fantasy", "Dr Who", "Jujutsu Kaisen", "Assassination Classroom", "Pikmin")
Write-Host "Searching database for target documents..." -ForegroundColor Yellow

foreach ($target in $targetNames) {
    try {
        $encodedTarget = [System.Web.HttpUtility]::UrlEncode($target)
        $url = "http://localhost:5248/browse-text?hasText=false&limit=50&search=$encodedTarget"
        Write-Host "Searching for: $target" -ForegroundColor Cyan
        
        $result = Invoke-RestMethod -Uri $url -Method GET -TimeoutSec 30
        if ($result -and $result.Count -gt 0) {
            foreach ($doc in $result) {
                if ($doc.name -like "*$target*") {
                    Write-Host "  FOUND: ID $($doc.id) - $($doc.name)" -ForegroundColor Green
                }
            }
        } else {
            Write-Host "  No matches found for $target" -ForegroundColor Gray
        }
    } catch {
        Write-Host "  Error searching for $target`: $($_.Exception.Message)" -ForegroundColor Red
    }
}
