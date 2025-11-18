#!/bin/bash
DB_PATH="/var/lib/jumpchain/jumpsearch.db"

echo "Syncing migration history..."
sqlite3 $DB_PATH "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251116164622_AddResourceKeyToDriveConfiguration', '8.0.0');"
sqlite3 $DB_PATH "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251116172120_AddParentDriveNameToDriveConfiguration', '8.0.0');"

echo "Current migrations:"
sqlite3 $DB_PATH "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;"
echo ""
echo "Done. Run ./deploy.sh now."