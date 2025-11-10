# SpaceBattles Genre Scraper with Selenium
# This handles JavaScript-rendered spoiler content

import json
import re
import time
from datetime import datetime

try:
    from selenium import webdriver
    from selenium.webdriver.common.by import By
    from selenium.webdriver.support.ui import WebDriverWait
    from selenium.webdriver.support import expected_conditions as EC
    from selenium.webdriver.chrome.options import Options
except ImportError:
    print("ERROR: Selenium not installed.")
    print("Install with: pip install selenium")
    print("\nAlso need ChromeDriver:")
    print("Download from: https://chromedriver.chromium.org/")
    print("Or install via: pip install webdriver-manager")
    exit(1)

url = "https://forums.spacebattles.com/threads/general-jumpchain-thread-the-13th.1124501/post-96370811"

print("Initializing browser...")

# Setup Chrome options
chrome_options = Options()
chrome_options.add_argument("--headless")  # Run in background
chrome_options.add_argument("--disable-gpu")
chrome_options.add_argument("--no-sandbox")
chrome_options.add_argument("--disable-dev-shm-usage")

try:
    # Try to use webdriver-manager if available
    try:
        from selenium.webdriver.chrome.service import Service
        from webdriver_manager.chrome import ChromeDriverManager
        service = Service(ChromeDriverManager().install())
        driver = webdriver.Chrome(service=service, options=chrome_options)
    except:
        # Fallback to system ChromeDriver
        driver = webdriver.Chrome(options=chrome_options)
    
    print(f"Fetching {url}...")
    driver.get(url)
    
    # Wait for page to load
    time.sleep(3)
    
    print("Finding spoiler sections...")
    
    # Find all spoiler elements
    spoilers = driver.find_elements(By.CLASS_NAME, "bbCodeSpoiler")
    
    print(f"Found {len(spoilers)} spoiler sections")
    
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
    
    for idx, spoiler in enumerate(spoilers):
        try:
            # Get spoiler title
            title_elem = spoiler.find_element(By.CLASS_NAME, "bbCodeSpoiler-title")
            genre_title = title_elem.text.strip().replace("Spoiler:", "").strip()
            
            print(f"\nProcessing: {genre_title}")
            
            # Click to expand spoiler
            title_elem.click()
            time.sleep(0.5)
            
            # Get spoiler content
            content_elem = spoiler.find_element(By.CLASS_NAME, "bbCodeSpoiler-content")
            
            # Find all links in the spoiler
            links = content_elem.find_elements(By.TAG_NAME, "a")
            
            document_names = []
            for link in links:
                link_text = link.text.strip()
                if link_text and len(link_text) > 2:
                    document_names.append(link_text)
            
            print(f"  Found {len(document_names)} documents")
            
            # Map to our genre categories
            mapped_genre = None
            if "slice of life" in genre_title.lower():
                mapped_genre = "Slice of Life"
            elif "histor" in genre_title.lower():
                mapped_genre = "Historical"
            elif "survival" in genre_title.lower():
                mapped_genre = "Survival"
            elif "modern adventure" in genre_title.lower():
                mapped_genre = "Modern Adventure"
            elif "military" in genre_title.lower():
                mapped_genre = "Military"
            elif "horror" in genre_title.lower():
                mapped_genre = "Horror"
            elif "super" in genre_title.lower():
                mapped_genre = "Super Hero"
            elif "science fiction" in genre_title.lower() or "sci-fi" in genre_title.lower():
                mapped_genre = "Science Fiction"
            elif "urban fantasy" in genre_title.lower():
                mapped_genre = "Modern Occult"
            elif "fantasy" in genre_title.lower():
                mapped_genre = "Fantasy"
            
            if mapped_genre:
                genre_mappings[mapped_genre] = document_names
                print(f"  Mapped to: {mapped_genre}")
            else:
                print(f"  WARNING: Could not map genre '{genre_title}'")
                genre_mappings[genre_title] = document_names
                
        except Exception as e:
            print(f"  Error processing spoiler {idx}: {e}")
            continue
    
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
    
    print("\n" + "="*60)
    print("SUCCESS! Scraped data saved to: genre-mappings-scraped.json")
    print("="*60)
    
    print("\nGenre Summary:")
    for genre in sorted(genre_mappings.keys()):
        count = len(genre_mappings[genre])
        print(f"  {genre}: {count} documents")
    
    print(f"\nTotal documents scraped: {sum(len(v) for v in genre_mappings.values())}")
    
except Exception as e:
    print(f"\nERROR: {e}")
    import traceback
    traceback.print_exc()
    exit(1)
