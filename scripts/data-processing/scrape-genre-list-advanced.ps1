# Advanced SpaceBattles Genre List Scraper
# This version tries multiple extraction methods

$url = "https://forums.spacebattles.com/threads/general-jumpchain-thread-the-13th.1124501/post-96370811"
$outputFile = "genre-mappings-scraped.json"

Write-Host "Attempting advanced scraping methods..." -ForegroundColor Cyan

try {
    # Method 1: Try with standard web request
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing
    $html = $response.Content
    
    Write-Host "Page content length: $($html.Length) characters" -ForegroundColor Yellow
    
    # Save raw HTML for inspection
    $html | Out-File -FilePath "forum-raw.html" -Encoding UTF8
    Write-Host "Saved raw HTML to forum-raw.html for inspection" -ForegroundColor Gray
    
    # Try different spoiler patterns
    $patterns = @(
        '(?s)<div[^>]*bbCodeSpoiler[^>]*>.*?<div[^>]*bbCodeSpoiler-title[^>]*>(.*?)</div>.*?<div[^>]*bbCodeSpoiler-content[^>]*>(.*?)</div>',
        '(?s)data-spoiler="(.*?)"[^>]*>(.*?)</div>',
        '(?s)<details[^>]*>\s*<summary[^>]*>(.*?)</summary>(.*?)</details>',
        '(?s)Spoiler:\s*([^\n]+)\n(.*?)(?=Spoiler:|$)'
    )
    
    $allMatches = @()
    foreach ($pattern in $patterns) {
        $matches = [regex]::Matches($html, $pattern)
        if ($matches.Count -gt 0) {
            Write-Host "Pattern matched: $($matches.Count) results with pattern index $($patterns.IndexOf($pattern))" -ForegroundColor Green
            $allMatches += $matches
        }
    }
    
    # If no spoilers found, try to extract links directly from the post content
    if ($allMatches.Count -eq 0) {
        Write-Host "No spoiler sections found. Attempting to extract post content directly..." -ForegroundColor Yellow
        
        # Find the post content div
        $postPattern = '(?s)<article[^>]*class="[^"]*message-body[^"]*"[^>]*>(.*?)</article>'
        $postMatch = [regex]::Match($html, $postPattern)
        
        if ($postMatch.Success) {
            $postContent = $postMatch.Groups[1].Value
            Write-Host "Found post content: $($postContent.Length) characters" -ForegroundColor Green
            
            # Save post content for inspection
            $postContent | Out-File -FilePath "post-content.html" -Encoding UTF8
            Write-Host "Saved post content to post-content.html" -ForegroundColor Gray
            
            # Try to find any structure indicating genres
            # Look for headers or bold text followed by links
            $sectionPattern = '(?s)<(?:h[1-6]|strong|b)[^>]*>([^<]+)</(?:h[1-6]|strong|b)>(.*?)(?=<(?:h[1-6]|strong|b)|$)'
            $sections = [regex]::Matches($postContent, $sectionPattern)
            
            Write-Host "Found $($sections.Count) potential sections" -ForegroundColor Yellow
        }
    }
    
    # If still nothing, we need to inform the user
    if ($allMatches.Count -eq 0) {
        Write-Host "`n========================================" -ForegroundColor Red
        Write-Host "UNABLE TO EXTRACT SPOILER CONTENT" -ForegroundColor Red
        Write-Host "========================================" -ForegroundColor Red
        Write-Host "`nThe forum page uses JavaScript to load spoiler content." -ForegroundColor Yellow
        Write-Host "This requires browser automation (Selenium) to access." -ForegroundColor Yellow
        Write-Host "`nAlternative solutions:" -ForegroundColor Cyan
        Write-Host "1. Install Selenium module: Install-Module Selenium -Scope CurrentUser" -ForegroundColor White
        Write-Host "2. Use the admin UI to manually paste the lists" -ForegroundColor White
        Write-Host "3. I can create a Python script with Selenium instead" -ForegroundColor White
        Write-Host "`nOpening the raw HTML file in your browser for manual inspection..." -ForegroundColor Gray
        
        # Open the files for user to inspect
        Start-Process "forum-raw.html"
        
        exit 1
    }
    
} catch {
    Write-Host "`nERROR: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
