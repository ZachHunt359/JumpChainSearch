#!/bin/bash
# Fix migration state in production database

DB_PATH="/var/lib/jumpchain/jumpsearch.db"

echo "Checking migration history..."
sqlite3 $DB_PATH "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;"

echo ""
echo "Manually marking ResourceKey migration as applied..."
sqlite3 $DB_PATH "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251116164622_AddResourceKeyToDriveConfiguration', '8.0.0');"

echo ""
echo "Updated migration history:"
sqlite3 $DB_PATH "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;"
