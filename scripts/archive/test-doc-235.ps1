# Query specific document by ID
try {
    $doc = Invoke-RestMethod -Uri "http://localhost:5248/get-extracted-text/235" -Method GET
    Write-Host "Document ID: $($doc.document.id)" -ForegroundColor Green
    Write-Host "Name: $($doc.document.name)" -ForegroundColor Green
    Write-Host "GoogleDriveFileId: Not available in this endpoint" -ForegroundColor Yellow
    
    # Let's try a different approach - check if document has been processed
    $hasText = $doc.document.hasExtractedText
    $textLength = $doc.document.extractedTextLength
    Write-Host "Has Text: $hasText, Length: $textLength" -ForegroundColor Cyan
    
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
