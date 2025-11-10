# Get failed document names only
$response = Invoke-RestMethod -Uri "http://localhost:5248/browse-text?hasText=false&limit=100" -Method GET
$failedDocs = $response.documents | Sort-Object id | Select-Object -ExpandProperty name
Write-Host "Failed Documents (names only):" -ForegroundColor Yellow
$failedDocs | ForEach-Object { Write-Host $_ }
