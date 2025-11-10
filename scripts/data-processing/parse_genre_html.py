# Parse genre lists directly from the downloaded HTML file
import re
import json
from datetime import datetime

print("Parsing forum-raw.html...")

with open("forum-raw.html", "r", encoding="utf-8") as f:
    html = f.read()

print(f"HTML file size: {len(html)} characters")

# Find all spoiler sections with their titles and content
spoiler_pattern = r'<span class="bbCodeSpoiler-button-title">([^<]+)</span>.*?<div class="bbCodeSpoiler-content">(.*?)</div>\s*</div>\s*</div>'

matches = re.findall(spoiler_pattern, html, re.DOTALL)

print(f"Found {len(matches)} spoiler sections\n")

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

genres_found = []

for title, content in matches:
    title = title.strip()
    print(f"Processing: {title}")
    
    # Find all Google Drive/Docs links in the content
    link_pattern = r'<a href="(https://(?:drive|docs)\.google\.com[^"]+)"[^>]*>([^<]+)</a>'
    links = re.findall(link_pattern, content)
    
    document_names = []
    for url, text in links:
        text = text.strip()
        # Decode HTML entities
        text = text.replace("&#039;", "'").replace("&amp;", "&").replace("&quot;", '"')
        if text and len(text) > 2:
            document_names.append(text)
    
    print(f"  Found {len(document_names)} Google Drive links")
    if document_names:
        print(f"  Sample: {', '.join(document_names[:3])}...")
    
    # Map to our genres
    mapped_genre = None
    title_lower = title.lower()
    
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
    
    if mapped_genre and document_names:
        genre_mappings[mapped_genre] = document_names
        genres_found.append(title)
        print(f"  ✓ Mapped to: {mapped_genre}\n")
    else:
        print(f"  Skipped (not a genre or no links)\n")

# Create JSON output
output = {
    "_comment": "Genre mappings parsed from SpaceBattles community list",
    "_source": "https://forums.spacebattles.com/threads/general-jumpchain-thread-the-13th.1124501/#post-96370811",
    "_scrapedAt": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
    "_note": "Urban Fantasy in their list = Modern Occult in our system",
    "_genresFound": genres_found,
    "genreMappings": genre_mappings
}

with open("genre-mappings-scraped.json", "w", encoding="utf-8") as f:
    json.dump(output, f, indent=2, ensure_ascii=False)

print("="*60)
print("SUCCESS! Genre mappings saved to: genre-mappings-scraped.json")
print("="*60)

print("\nGenre Summary:")
total_docs = 0
for genre in sorted(genre_mappings.keys()):
    count = len(genre_mappings[genre])
    total_docs += count
    status = "✓" if count > 0 else "✗"
    print(f"  [{status}] {genre}: {count} documents")

print(f"\nTotal documents: {total_docs}")
print(f"Genres mapped: {len(genres_found)}/10")

if total_docs > 0:
    print("\n✓ Success! Ready to apply genre tags.")
else:
    print("\n⚠️ No documents found - check HTML structure")
