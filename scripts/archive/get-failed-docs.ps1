# Get documents that have been processed but failed text extraction
Write-Host "Fetching documents that failed text extraction..." -ForegroundColor Yellow

# This endpoint should show us documents with empty extracted text (processed but no text found)
try {
    $result = Invoke-RestMethod -Uri "http://localhost:5248/browse-text?hasText=false&limit=50" -Method GET
    
    Write-Host "=== DOCUMENTS THAT FAILED TEXT EXTRACTION ===" -ForegroundColor Red
    Write-Host "Total found: $($result.totalCount)" -ForegroundColor White
    Write-Host ""
    
    $counter = 1
    foreach ($doc in $result.documents) {
        Write-Host "$counter. $($doc.name)" -ForegroundColor White
        Write-Host "   ID: $($doc.id), Size: $($doc.size) bytes, MIME: $($doc.mimeType)" -ForegroundColor Gray
        Write-Host "   Google Drive ID: $($doc.googleDriveFileId)" -ForegroundColor Gray
        Write-Host "   Source: $($doc.sourceDrive)" -ForegroundColor Gray
        Write-Host ""
        $counter++
    }
} catch {
    Write-Host "Error fetching failed documents: $($_.Exception.Message)" -ForegroundColor Red
}
