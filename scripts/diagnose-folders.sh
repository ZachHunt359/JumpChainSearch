#!/bin/bash
# Diagnostic script to troubleshoot folder discovery issues

echo "========================================="
echo "JumpChain Folder Discovery Diagnostics"
echo "========================================="
echo ""

# 1. Check if GOOGLE_API_KEY is set
echo "1. Checking GOOGLE_API_KEY environment variable..."
if [ -z "$GOOGLE_API_KEY" ]; then
    echo "   ❌ GOOGLE_API_KEY is NOT set"
    echo "   This is likely why folder discovery is failing!"
else
    echo "   ✅ GOOGLE_API_KEY is set (length: ${#GOOGLE_API_KEY} characters)"
fi
echo ""

# 2. Check if service account file exists
echo "2. Checking Google service account file..."
if [ -f "/var/lib/jumpchain/service-account.json" ]; then
    echo "   ✅ Service account file exists"
else
    echo "   ⚠️  Service account file not found at /var/lib/jumpchain/service-account.json"
fi
echo ""

# 3. Check database for folder counts
echo "3. Checking database for existing folders..."
DB_PATH="/var/lib/jumpchain/jumpsearch.db"
if [ -f "$DB_PATH" ]; then
    FOLDER_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM FolderConfigurations;")
    echo "   Total folders in database: $FOLDER_COUNT"
    
    if [ "$FOLDER_COUNT" -gt 0 ]; then
        echo ""
        echo "   Folders by drive:"
        sqlite3 "$DB_PATH" "SELECT dc.DriveName, COUNT(fc.Id) as FolderCount FROM DriveConfigurations dc LEFT JOIN FolderConfigurations fc ON dc.Id = fc.DriveConfigurationId GROUP BY dc.DriveName;"
    fi
else
    echo "   ❌ Database not found at $DB_PATH"
fi
echo ""

# 4. Check application logs for errors
echo "4. Checking recent application logs for folder-related errors..."
if [ -f "/var/lib/jumpchain/logs/app.log" ]; then
    echo "   Recent folder discovery errors:"
    grep -i "folder\|GOOGLE_API_KEY" /var/lib/jumpchain/logs/app.log | tail -10
else
    echo "   ⚠️  Application log not found"
    echo "   Try checking: journalctl -u jumpchain -n 50 | grep -i folder"
fi
echo ""

# 5. Check drive configurations
echo "5. Checking active drive configurations..."
sqlite3 "$DB_PATH" "SELECT Id, DriveName, IsActive FROM DriveConfigurations;"
echo ""

# 6. Check last scan times
echo "6. Checking last scan times..."
sqlite3 "$DB_PATH" "SELECT DriveName, LastScanTime FROM DriveConfigurations WHERE LastScanTime IS NOT NULL ORDER BY LastScanTime DESC LIMIT 5;"
echo ""

echo "========================================="
echo "Diagnostic Summary"
echo "========================================="
echo ""
echo "If GOOGLE_API_KEY is not set, add it to /etc/systemd/system/jumpchain.service:"
echo "  Environment=\"GOOGLE_API_KEY=your_api_key_here\""
echo ""
echo "Then reload and restart the service:"
echo "  sudo systemctl daemon-reload"
echo "  sudo systemctl restart jumpchain"
echo ""