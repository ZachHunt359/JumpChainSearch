try {
    $response = Invoke-WebRequest -Uri 'http://localhost:5248/direct-drive-export/1LjxRnjKon0XNtOVHSwaAnr5SmI1YCJ9X' -TimeoutSec 30
    $content = $response.Content | ConvertFrom-Json
    Write-Host "SUCCESS! Extraction Method: $($content.method)"
    Write-Host "Text Length: $($content.textLength)"
    Write-Host "First 300 characters:"
    Write-Host $content.textPreview.Substring(0, [Math]::Min(300, $content.textPreview.Length))
    Write-Host ""
    Write-Host "Checking for proper spacing..."
    if ($content.textPreview -match '\s') {
        Write-Host "✓ SPACES DETECTED - Improved extraction working!"
    } else {
        Write-Host "✗ NO SPACES - Still using old method"
    }
} catch {
    Write-Host "Error: $($_.Exception.Message)"
}
