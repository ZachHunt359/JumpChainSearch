# Improved SpaceBattles Genre Scraper - Focus on first 10 spoilers
import json
import time
from datetime import datetime

try:
    from selenium import webdriver
    from selenium.webdriver.common.by import By
    from selenium.webdriver.support.ui import WebDriverWait
    from selenium.webdriver.support import expected_conditions as EC
    from selenium.webdriver.chrome.options import Options
    from selenium.webdriver.common.action_chains import ActionChains
except ImportError:
    print("ERROR: Selenium not installed")
    exit(1)

url = "https://forums.spacebattles.com/threads/general-jumpchain-thread-the-13th.1124501/post-96370811"

print("Initializing browser...")

chrome_options = Options()
# Remove headless mode to see what's happening
# chrome_options.add_argument("--headless")
chrome_options.add_argument("--disable-gpu")
chrome_options.add_argument("--no-sandbox")
chrome_options.add_argument("--window-size=1920,1080")

try:
    from selenium.webdriver.chrome.service import Service
    from webdriver_manager.chrome import ChromeDriverManager
    service = Service(ChromeDriverManager().install())
    driver = webdriver.Chrome(service=service, options=chrome_options)
except:
    driver = webdriver.Chrome(options=chrome_options)

try:
    print(f"Fetching {url}...")
    driver.get(url)
    
    # Wait for page to fully load
    wait = WebDriverWait(driver, 10)
    wait.until(EC.presence_of_element_located((By.CLASS_NAME, "message-body")))
    
    print("Page loaded. Scrolling to post...")
    time.sleep(2)
    
    # Find the specific post
    post = driver.find_element(By.CSS_SELECTOR, "article.message")
    driver.execute_script("arguments[0].scrollIntoView(true);", post)
    time.sleep(1)
    
    # Find all spoilers in the post
    spoilers = post.find_elements(By.CLASS_NAME, "bbCodeSpoiler")
    
    print(f"Found {len(spoilers)} total spoilers")
    print("Processing ONLY the first 10 spoilers (genre lists)...")
    
    genre_mappings = {
        "Slice of Life": [],
        "Historical": [],
        "Survival": [],
        "Modern Adventure": [],
        "Military": [],
        "Horror": [],
        "Super Hero": [],
        "Science Fiction": [],
        "Modern Occult": [],
        "Fantasy": []
    }
    
    # Process only first 10 spoilers
    for idx in range(min(10, len(spoilers))):
        spoiler = spoilers[idx]
        
        try:
            # Scroll to spoiler
            driver.execute_script("arguments[0].scrollIntoView({block: 'center'});", spoiler)
            time.sleep(0.5)
            
            # Get title BEFORE clicking
            title_elem = spoiler.find_element(By.CLASS_NAME, "bbCodeSpoiler-title")
            genre_title = title_elem.text.strip().replace("Spoiler:", "").strip()
            
            print(f"\n[{idx+1}/10] Processing: {genre_title}")
            
            # Check if already expanded
            content_div = spoiler.find_element(By.CLASS_NAME, "bbCodeSpoiler-content")
            is_hidden = "none" in content_div.get_attribute("style") or not content_div.is_displayed()
            
            if is_hidden:
                print(f"  Clicking to expand...")
                # Use JavaScript click to avoid interception issues
                driver.execute_script("arguments[0].click();", title_elem)
                time.sleep(1)  # Wait for expansion animation
            else:
                print(f"  Already expanded")
            
            # Now get the content
            content_div = spoiler.find_element(By.CLASS_NAME, "bbCodeSpoiler-content")
            
            # Get all text from the content (not just links)
            full_text = content_div.text
            print(f"  Content length: {len(full_text)} characters")
            
            # Find all links
            links = content_div.find_elements(By.TAG_NAME, "a")
            
            document_names = []
            for link in links:
                link_text = link.text.strip()
                if link_text and len(link_text) > 2:
                    # Filter out navigation links
                    href = link.get_attribute("href") or ""
                    if "drive.google.com" in href or "docs.google.com" in href:
                        document_names.append(link_text)
            
            print(f"  Found {len(document_names)} document links")
            
            # Show first few documents for verification
            if document_names:
                print(f"  Sample: {', '.join(document_names[:3])}...")
            
            # Map to our genre categories
            mapped_genre = None
            title_lower = genre_title.lower()
            
            if "slice of life" in title_lower:
                mapped_genre = "Slice of Life"
            elif "histor" in title_lower or "lost world" in title_lower:
                mapped_genre = "Historical"
            elif "survival" in title_lower:
                mapped_genre = "Survival"
            elif "modern adventure" in title_lower:
                mapped_genre = "Modern Adventure"
            elif "military" in title_lower:
                mapped_genre = "Military"
            elif "horror" in title_lower:
                mapped_genre = "Horror"
            elif "super" in title_lower:
                mapped_genre = "Super Hero"
            elif "science fiction" in title_lower or "sci-fi" in title_lower:
                mapped_genre = "Science Fiction"
            elif "urban fantasy" in title_lower:
                mapped_genre = "Modern Occult"
            elif "fantasy" in title_lower and "urban" not in title_lower:
                mapped_genre = "Fantasy"
            
            if mapped_genre:
                genre_mappings[mapped_genre] = document_names
                print(f"  ✓ Mapped to: {mapped_genre}")
            else:
                print(f"  WARNING: Could not map '{genre_title}'")
                # Store with original name
                genre_mappings[genre_title] = document_names
                
        except Exception as e:
            print(f"  ERROR: {e}")
            continue
    
    print("\n" + "="*60)
    print("Closing browser...")
    driver.quit()
    
    # Create JSON output
    output = {
        "_comment": "Genre mappings scraped from SpaceBattles community list",
        "_source": url,
        "_scrapedAt": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "_note": "Urban Fantasy in their list = Modern Occult in our system",
        "genreMappings": genre_mappings
    }
    
    with open("genre-mappings-scraped.json", "w", encoding="utf-8") as f:
        json.dump(output, f, indent=2, ensure_ascii=False)
    
    print("="*60)
    print("SUCCESS! Scraped data saved to: genre-mappings-scraped.json")
    print("="*60)
    
    print("\nGenre Summary:")
    total_docs = 0
    for genre in sorted(genre_mappings.keys()):
        count = len(genre_mappings[genre])
        total_docs += count
        status = "✓" if count > 0 else "✗"
        print(f"  [{status}] {genre}: {count} documents")
    
    print(f"\nTotal documents scraped: {total_docs}")
    
    if total_docs == 0:
        print("\n⚠️ WARNING: No documents found!")
        print("The spoilers may require different interaction or the content structure changed.")
    
except Exception as e:
    print(f"\nFATAL ERROR: {e}")
    import traceback
    traceback.print_exc()
    if 'driver' in locals():
        driver.quit()
    exit(1)
