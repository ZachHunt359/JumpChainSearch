# SpaceBattles Genre List Scraper
# Fetches and parses the community genre list from the forum post

$url = "https://forums.spacebattles.com/threads/general-jumpchain-thread-the-13th.1124501/post-96370811"
$outputFile = "genre-mappings-scraped.json"

Write-Host "Fetching forum post..." -ForegroundColor Cyan

try {
    # Fetch the page content
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing
    $html = $response.Content
    
    Write-Host "Page fetched successfully. Parsing spoiler sections..." -ForegroundColor Green
    
    # Initialize genre mappings
    $genreMappings = @{
        "Slice of Life" = @()
        "Historical" = @()
        "Survival" = @()
        "Modern Adventure" = @()
        "Military" = @()
        "Horror" = @()
        "Super Hero" = @()
        "Science Fiction" = @()
        "Modern Occult" = @()  # Will map from "Urban Fantasy"
        "Fantasy" = @()
    }
    
    # Extract spoiler sections - they are in the format:
    # Spoiler: Genre Name
    # Content with links...
    
    # Use regex to find spoiler sections
    $spoilerPattern = '(?s)<div[^>]*class="[^"]*bbCodeSpoiler[^"]*"[^>]*>.*?<div[^>]*class="[^"]*bbCodeSpoiler-title[^"]*"[^>]*>(.*?)</div>.*?<div[^>]*class="[^"]*bbCodeSpoiler-content[^"]*"[^>]*>(.*?)</div>'
    
    $matches = [regex]::Matches($html, $spoilerPattern)
    
    Write-Host "Found $($matches.Count) spoiler sections" -ForegroundColor Yellow
    
    foreach ($match in $matches) {
        $genreTitle = $match.Groups[1].Value -replace '<[^>]+>', '' -replace '&nbsp;', ' ' -replace '\s+', ' ' -replace 'Spoiler:\s*', '' -replace '^\s+|\s+$', ''
        $content = $match.Groups[2].Value
        
        Write-Host "`nProcessing: $genreTitle" -ForegroundColor Magenta
        
        # Extract links and their text from content
        $linkPattern = '<a[^>]*href="([^"]+)"[^>]*>([^<]+)</a>'
        $linkMatches = [regex]::Matches($content, $linkPattern)
        
        $documentNames = @()
        foreach ($link in $linkMatches) {
            $linkText = $link.Groups[2].Value -replace '&nbsp;', ' ' -replace '\s+', ' ' -replace '^\s+|\s+$', ''
            if ($linkText -and $linkText.Length -gt 2) {
                $documentNames += $linkText
            }
        }
        
        Write-Host "  Found $($documentNames.Count) documents" -ForegroundColor Gray
        
        # Map genre titles to our categories
        $mappedGenre = $null
        switch -Wildcard ($genreTitle) {
            "*Slice of Life*" { $mappedGenre = "Slice of Life" }
            "*History*" { $mappedGenre = "Historical" }
            "*Historical*" { $mappedGenre = "Historical" }
            "*Survival*" { $mappedGenre = "Survival" }
            "*Modern Adventure*" { $mappedGenre = "Modern Adventure" }
            "*Military*" { $mappedGenre = "Military" }
            "*Horror*" { $mappedGenre = "Horror" }
            "*Super*" { $mappedGenre = "Super Hero" }
            "*Superhero*" { $mappedGenre = "Super Hero" }
            "*Science Fiction*" { $mappedGenre = "Science Fiction" }
            "*Sci-Fi*" { $mappedGenre = "Science Fiction" }
            "*Urban Fantasy*" { $mappedGenre = "Modern Occult" }
            "*Fantasy*" { $mappedGenre = "Fantasy" }
        }
        
        if ($mappedGenre -and $documentNames.Count -gt 0) {
            $genreMappings[$mappedGenre] = $documentNames
            Write-Host "  Mapped to: $mappedGenre" -ForegroundColor Green
        } else {
            Write-Host "  WARNING: Could not map genre '$genreTitle'" -ForegroundColor Red
            # Create a new category for unmapped genres
            if ($documentNames.Count -gt 0) {
                $genreMappings[$genreTitle] = $documentNames
            }
        }
    }
    
    # Create JSON output
    $output = @{
        "_comment" = "Genre mappings scraped from SpaceBattles community list"
        "_source" = $url
        "_scrapedAt" = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        "_note" = "Urban Fantasy in their list = Modern Occult in our system"
        "genreMappings" = $genreMappings
    }
    
    $json = $output | ConvertTo-Json -Depth 10
    $json | Out-File -FilePath $outputFile -Encoding UTF8
    
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "SUCCESS! Scraped data saved to: $outputFile" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    
    # Display summary
    Write-Host "`nGenre Summary:" -ForegroundColor Yellow
    foreach ($genre in $genreMappings.Keys | Sort-Object) {
        $count = $genreMappings[$genre].Count
        Write-Host "  $genre : $count documents" -ForegroundColor $(if ($count -gt 0) { "Green" } else { "Gray" })
    }
    
} catch {
    Write-Host "`nERROR: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
