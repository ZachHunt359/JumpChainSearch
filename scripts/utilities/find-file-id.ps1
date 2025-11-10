# Find a document with empty extracted text to get its file ID
$docs = Invoke-RestMethod -Uri "http://localhost:5248/browse-text?hasText=false&limit=10" -Method GET
$firstDoc = $docs.documents[0]
Write-Host "Testing document: $($firstDoc.name) (ID: $($firstDoc.id))" -ForegroundColor Green

# We need to find the Google Drive file ID - let's check what endpoints return it
# Based on the grep results, let's try extracting directly
try {
    $result = Invoke-RestMethod -Uri "http://localhost:5248/test-extract-few/1" -Method POST
    $testDoc = $result.results | Where-Object { $_.name -like "*Thunderbolt*" }
    if ($testDoc) {
        Write-Host "Found Thunderbolt document in test results:" -ForegroundColor Yellow
        $testDoc | Format-List
    } else {
        Write-Host "Thunderbolt not in first batch, testing first available failed doc..." -ForegroundColor Yellow
        $firstFailedDoc = $result.results | Select-Object -First 1
        $firstFailedDoc | Format-List
    }
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
} 
