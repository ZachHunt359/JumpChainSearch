#!/bin/bash
#
# JumpChain Search Deployment Script
# Run this on your Ubuntu VPS to pull updates and restart the service
#
# Usage: 
#   ./deploy.sh             # Deploy to production
#   ./deploy.sh --staging   # Deploy to staging environment

set -e  # Exit on any error

# ==========================================
# Environment Configuration
# ==========================================

# Default to production
ENVIRONMENT="production"
if [ "$1" == "--staging" ]; then
    ENVIRONMENT="staging"
fi

# Environment-specific settings
if [ "$ENVIRONMENT" == "staging" ]; then
    SERVICE_NAME="jumpchain-staging"
    DEPLOY_DIR="/opt/jumpchain-staging"
    DB_PATH="/var/lib/jumpchain/jumpsearch-staging.db"
    PORT=5001
    DOMAIN="dev.jumpchainsearch.app"
else
    SERVICE_NAME="jumpchain"
    DEPLOY_DIR="/home/deploy/JumpChainSearch"
    DB_PATH="/var/lib/jumpchain/jumpsearch.db"
    PORT=5248
    DOMAIN="jumpchainsearch.app"
fi

echo "======================================"
echo "JumpChain Search - Deployment Script"
echo "Environment: $ENVIRONMENT"
echo "======================================"
echo ""

# Production safety check
if [ "$ENVIRONMENT" == "production" ]; then
    echo "⚠️  DEPLOYING TO PRODUCTION ($DOMAIN)"
    read -p "Continue? (y/n) " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Deployment cancelled."
        exit 0
    fi
    echo ""
fi

# Request sudo password upfront to minimize downtime later
echo "Requesting sudo access (needed for service restart)..."
sudo -v
echo "✓ Sudo access granted"
echo ""

# Keep sudo alive in background (refreshes every 60 seconds)
while true; do sudo -n true; sleep 60; kill -0 "$$" || exit; done 2>/dev/null &

# Configuration
APP_DIR=$(pwd)
PUBLISH_DIR="$DEPLOY_DIR/publish"
BRANCH="main"

# Database connection
DB_CONNECTION="Data Source=$DB_PATH;Mode=ReadWrite"
echo "Service: $SERVICE_NAME"
echo "Deploy to: $DEPLOY_DIR"
echo "Database: $DB_PATH"
echo "Port: $PORT"
echo ""

# Check if we're in the right directory
if [ ! -f "JumpChainSearch.csproj" ]; then
    echo "Error: JumpChainSearch.csproj not found. Are you in the project directory?"
    exit 1
fi

echo "Step 1: Stopping the service..."
sudo systemctl stop $SERVICE_NAME
echo "✓ Service stopped"
echo ""

echo "Step 2: Cleaning previous build..."
sudo rm -rf "$PUBLISH_DIR"
sudo rm -rf "$APP_DIR/obj"
sudo rm -rf "$APP_DIR/bin"
echo "✓ Previous build cleaned"
echo ""

echo "Step 3: Ensuring deployment directory exists..."
sudo mkdir -p "$DEPLOY_DIR"
sudo chown $USER:$USER "$DEPLOY_DIR"
echo "✓ Deployment directory ready"
echo ""

echo "Step 4: Pulling latest changes from Git..."
git fetch origin
git reset --hard origin/$BRANCH
echo "✓ Code updated to latest commit"
echo ""

echo "Step 5: Backing up database..."
# Extract database file path from connection string
BACKUP_FILE="$DB_PATH.backup-$(date +%Y%m%d-%H%M%S)"
if [ -f "$DB_PATH" ]; then
    cp "$DB_PATH" "$BACKUP_FILE"
    echo "✓ Database backed up to: $BACKUP_FILE"
else
    echo "⚠ No database file found at: $DB_PATH (first deployment?)"
fi
echo ""

echo "Step 6: Building the application..."
dotnet publish -c Release -o "$PUBLISH_DIR"
echo "✓ Build completed"
echo ""

echo "Step 6.5: Manually copying critical config files..."
# Manually copy files to ensure they're always updated (dotnet publish cache can be stubborn)
cp -f "$APP_DIR/series-mappings.json" "$PUBLISH_DIR/" 2>/dev/null && echo "✓ Copied series-mappings.json" || echo "⚠ Failed to copy series-mappings.json"
cp -f "$APP_DIR/genre-mappings-scraped.json" "$PUBLISH_DIR/" 2>/dev/null && echo "✓ Copied genre-mappings-scraped.json" || echo "⚠ Failed to copy genre-mappings-scraped.json"
cp -f "$APP_DIR/appsettings.json" "$PUBLISH_DIR/" 2>/dev/null && echo "✓ Copied appsettings.json" || echo "⚠ Failed to copy appsettings.json"
echo ""

echo "Step 6.6: Validating deployment files..."
VALIDATION_FAILED=0

# Critical files that must be copied from repo to publish/
declare -A REQUIRED_FILES=(
    ["series-mappings.json"]="Series tagging configuration"
    ["genre-mappings-scraped.json"]="Genre tagging configuration"
    ["appsettings.json"]="Application configuration"
)

for file in "${!REQUIRED_FILES[@]}"; do
    SOURCE_FILE="$APP_DIR/$file"
    DEST_FILE="$PUBLISH_DIR/$file"
    DESCRIPTION="${REQUIRED_FILES[$file]}"
    
    if [ ! -f "$SOURCE_FILE" ]; then
        echo "✗ MISSING SOURCE: $file ($DESCRIPTION)"
        echo "  Expected at: $SOURCE_FILE"
        VALIDATION_FAILED=1
        continue
    fi
    
    if [ ! -f "$DEST_FILE" ]; then
        echo "✗ NOT COPIED: $file ($DESCRIPTION)"
        echo "  Source exists but not in publish/"
        echo "  Check JumpChainSearch.csproj <Content Update> configuration"
        VALIDATION_FAILED=1
        continue
    fi
    
    # Compare file sizes to detect incomplete copies
    SOURCE_SIZE=$(stat -f%z "$SOURCE_FILE" 2>/dev/null || stat -c%s "$SOURCE_FILE" 2>/dev/null)
    DEST_SIZE=$(stat -f%z "$DEST_FILE" 2>/dev/null || stat -c%s "$DEST_FILE" 2>/dev/null)
    
    if [ "$SOURCE_SIZE" != "$DEST_SIZE" ]; then
        echo "✗ SIZE MISMATCH: $file ($DESCRIPTION)"
        echo "  Source: $SOURCE_SIZE bytes"
        echo "  Publish: $DEST_SIZE bytes"
        echo "  File may not have updated correctly"
        VALIDATION_FAILED=1
    else
        echo "✓ $file ($SOURCE_SIZE bytes) - $DESCRIPTION"
    fi
done

if [ $VALIDATION_FAILED -eq 1 ]; then
    echo ""
    echo "✗ Deployment validation FAILED!"
    echo "Some required files are missing or incorrect in publish/"
    echo ""
    echo "Common fixes:"
    echo "  1. Ensure JumpChainSearch.csproj has <Content Update> entries"
    echo "  2. Use 'Always' not 'PreserveNewest' for CopyToOutputDirectory"
    echo "  3. Check that source files exist in repository"
    exit 1
fi
echo "✓ All deployment files validated"
echo ""

echo "Step 6.7: Setting up database directory permissions..."
# Ensure database directory exists with proper permissions for migrations
DB_DIR=$(dirname "$DB_PATH")
sudo mkdir -p "$DB_DIR"

# Set ownership: deploy user (for migrations) + www-data group (for service)
sudo chown $USER:www-data "$DB_DIR"
sudo chmod 775 "$DB_DIR"
echo "✓ Database directory ready: $DB_DIR"
echo ""

echo "Step 7: Applying database migrations..."
# Ensure dotnet-ef is in PATH
export PATH="$PATH:$HOME/.dotnet/tools"
if command -v dotnet-ef &> /dev/null; then
    cd "$APP_DIR"
    # Use the same connection string as the production service
    export ConnectionStrings__DefaultConnection="$DB_CONNECTION"
    dotnet ef database update
    
    # After migration, ensure database file has correct permissions for www-data
    if [ -f "$DB_PATH" ]; then
        sudo chown www-data:www-data "$DB_PATH"
        sudo chmod 664 "$DB_PATH"
        # Also fix any SQLite journal files
        sudo chown www-data:www-data "$DB_PATH"-* 2>/dev/null || true
        echo "✓ Migrations applied to $DB_PATH"
    else
        echo "⚠ Database file not created at $DB_PATH"
    fi
else
    echo "⚠ dotnet-ef not found. Install with: dotnet tool install --global dotnet-ef"
    echo "⚠ Skipping migrations - you may need to run manually"
fi
echo ""

echo "Step 8: Starting the service..."
sudo systemctl start $SERVICE_NAME
echo "✓ Service started"
echo ""

echo "Step 9: Checking service status..."
sleep 2
if sudo systemctl is-active --quiet $SERVICE_NAME; then
    echo "✓ Service is running"
    sudo systemctl status $SERVICE_NAME --no-pager -l | head -n 15
else
    echo "✗ Service failed to start!"
    echo "Checking logs..."
    sudo journalctl -u $SERVICE_NAME -n 50 --no-pager
    exit 1
fi

echo ""
echo "Step 10: Initializing scan schedule..."
sleep 3  # Give the service time to fully start
INIT_RESPONSE=$(curl -s -X POST http://localhost:$PORT/admin/system/scan-schedule/init)
if echo "$INIT_RESPONSE" | grep -q '"success":true'; then
    echo "✓ Scan schedule initialized"
    echo "$INIT_RESPONSE" | grep -o '"nextScheduledScan":"[^"]*"' || true
    
    # Restart service to reload configuration
    echo ""
    echo "Step 11: Restarting service to apply scan schedule..."
    sudo systemctl restart $SERVICE_NAME
    sleep 2
    if sudo systemctl is-active --quiet $SERVICE_NAME; then
        echo "✓ Service restarted successfully"
    else
        echo "✗ Service failed to restart!"
        sudo journalctl -u $SERVICE_NAME -n 20 --no-pager
        exit 1
    fi
else
    echo "⚠ Failed to initialize scan schedule (you can enable it manually in admin panel)"
    echo "Response: $INIT_RESPONSE"
fi

echo ""
echo "======================================"
echo "Deployment completed successfully!"
echo "======================================"
echo ""
echo "Useful commands:"
echo "  View logs:    sudo journalctl -u $SERVICE_NAME -f"
echo "  Stop service: sudo systemctl stop $SERVICE_NAME"
echo "  Restart:      sudo systemctl restart $SERVICE_NAME"
echo "  Status:       sudo systemctl status $SERVICE_NAME"