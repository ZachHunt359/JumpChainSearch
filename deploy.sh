#!/bin/bash
#
# JumpChain Search Deployment Script
# Run this on your Ubuntu VPS to pull updates and restart the service
#
# Usage: ./deploy.sh

set -e  # Exit on any error

echo "======================================"
echo "JumpChain Search - Deployment Script"
echo "======================================"
echo ""

# Configuration
APP_DIR=$(pwd)
PUBLISH_DIR="$APP_DIR/publish"
SERVICE_NAME="jumpchain"
BRANCH="main"

# Check if we're in the right directory
if [ ! -f "JumpChainSearch.csproj" ]; then
    echo "Error: JumpChainSearch.csproj not found. Are you in the project directory?"
    exit 1
fi

echo "Step 1: Pulling latest changes from Git..."
git fetch origin
git reset --hard origin/$BRANCH
echo "✓ Code updated to latest commit"
echo ""

echo "Step 2: Backing up database..."
BACKUP_FILE="$APP_DIR/jumpchain.db.backup-$(date +%Y%m%d-%H%M%S)"
if [ -f "$APP_DIR/jumpchain.db" ]; then
    cp "$APP_DIR/jumpchain.db" "$BACKUP_FILE"
    echo "✓ Database backed up to: $BACKUP_FILE"
else
    echo "⚠ No database file found (first deployment?)"
fi
echo ""

echo "Step 3: Stopping the service..."
sudo systemctl stop $SERVICE_NAME
echo "✓ Service stopped"
echo ""

echo "Step 4: Cleaning previous build..."
rm -rf "$PUBLISH_DIR"
echo "✓ Previous build cleaned"
echo ""

echo "Step 5: Building the application..."
dotnet publish -c Release -o "$PUBLISH_DIR"
echo "✓ Build completed"
echo ""

echo "Step 5.5: Creating database symlink..."
if [ -f "$APP_DIR/jumpchain.db" ]; then
    ln -sf "$APP_DIR/jumpchain.db" "$PUBLISH_DIR/jumpchain.db"
    echo "✓ Database symlink created"
else
    echo "⚠ No database file found - symlink not created"
fi
echo ""

echo "Step 6: Applying database migrations..."
# Ensure dotnet-ef is in PATH
export PATH="$PATH:$HOME/.dotnet/tools"
if command -v dotnet-ef &> /dev/null; then
    cd "$APP_DIR"
    dotnet ef database update
    echo "✓ Migrations applied"
else
    echo "⚠ dotnet-ef not found. Install with: dotnet tool install --global dotnet-ef"
    echo "⚠ Skipping migrations - you may need to run manually"
fi
echo ""

echo "Step 7: Starting the service..."
sudo systemctl start $SERVICE_NAME
echo "✓ Service started"
echo ""

echo "Step 8: Checking service status..."
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
echo "======================================"
echo "Deployment completed successfully!"
echo "======================================"
echo ""
echo "Useful commands:"
echo "  View logs:    sudo journalctl -u $SERVICE_NAME -f"
echo "  Stop service: sudo systemctl stop $SERVICE_NAME"
echo "  Restart:      sudo systemctl restart $SERVICE_NAME"
echo "  Status:       sudo systemctl status $SERVICE_NAME"