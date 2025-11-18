# JumpChain Search

JumpChain Search is a modern web application for searching, reviewing, and managing JumpChain documents across multiple Google Drives. It is designed for both the JumpChain community (searchers, reviewers) and as a showcase of best-practice, modular ASP.NET Core Blazor Server architecture.

---

## For JumpChain Community Members

**JumpChain Search** helps you find, explore, and review JumpChain documents from across the community’s shared Google Drives.

### How to Use

- **Search**: Enter keywords to search across all indexed documents (Google Docs, PDFs, Word, etc.).
- **Filter**: Narrow results by drive, tags, document type, or folder.
- **Tagging**: Documents are automatically tagged by drive, folder, and content. You can also add or suggest tags.
- **Flag for Review**: If you find a document with incorrect or incomplete extracted text, use the “Needs Review” button to flag it for moderators.
- **Mobile Friendly**: The interface works on desktop, tablet, and mobile.

### Support & Feedback

- For issues, suggestions, or to help improve the document database, [open an issue on GitHub](https://github.com/ZachHunt359/JumpChainSearch/issues) or join the JumpChain community discussions.

---

## For Developers & Employers

This project demonstrates:

- **Modular, maintainable ASP.NET Core Blazor Server architecture**
- **Robust Google Drive API integration**
- **Entity Framework Core with SQLite/PostgreSQL**
- **Production-grade patterns: extension-based startup, CLI admin tools, and high-performance document counting**

### Project Structure (2025)

```
JumpChainSearch/
├── Data/                  # Entity Framework DbContext
├── Models/                # Data models (JumpDocument, DocumentTag, etc.)
├── Pages/                 # Blazor pages (UI)
├── Services/              # Business logic (GoogleDriveService, SearchService, etc.)
├── Extensions/            # Modular endpoint/service/startup extensions
├── Helpers/               # CLI and utility helpers
├── wwwroot/               # Static files (CSS, icons, etc.)
├── Program.cs             # Minimal startup, delegates to extensions/helpers
├── appsettings.json       # Configuration
└── .github/reference/     # Technical documentation (.md files)
```

### Modular Architecture

- **Startup**: All service registration, endpoint mapping, and startup logic is extracted to `Extensions/` and `Helpers/`.
- **CLI**: Admin user and maintenance commands are handled via `Helpers/CliAdminCommands.cs`.
- **No direct logic in `Program.cs`**: Only orchestration and extension method calls.
- **Documentation**: All technical docs are in `.github/reference/`.

### Development Guidelines

1. **Add new endpoints/services**: Use the appropriate extension file in `Extensions/`.
2. **Business logic**: Place in `Services/`.
3. **Models**: Add to `Models/` and update `Data/JumpChainDbContext.cs`.
4. **UI**: Add/modify Blazor pages in `Pages/`.
5. **CLI logic**: Add to `Helpers/`.
6. **Tests**: (Recommended) Add tests for new features and logic.
7. **Documentation**: Update `.github/reference/` and `README.md` as needed.

---

## Features

- **Multi-Drive Search**: Search across multiple Google Drives simultaneously
- **Smart Tagging**: Automatic and user-driven tagging
- **Full-Text Search**: Search document contents (Google Docs, PDFs, Word)
- **Advanced Filtering**: By drive, tags, type, and more
- **Real-time Results**: Fast, responsive UI
- **Mobile Friendly**: Works on all devices
- **Flag/Review System**: Community-driven quality control

## Technology Stack

- **Backend**: ASP.NET Core 8.0 (Blazor Server)
- **Database**: SQLite (dev) / PostgreSQL (prod)
- **ORM**: Entity Framework Core
- **API**: Google Drive API v3
- **Frontend**: Blazor + Bootstrap 5
- **Deployment**: Cross-platform (Windows, Linux, macOS)

## Setup & Deployment

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Google Cloud Project with Drive API enabled
- Service Account (prod) or OAuth2 (dev) credentials

### Quick Start

```bash
git clone <repository-url>
cd JumpChainSearch
dotnet restore
dotnet build
dotnet run
```

### Deployment Script

For production servers, use the included `deploy.sh` script to pull updates and restart the service:

```bash
# On your Ubuntu VPS, in the project directory:
chmod +x deploy.sh
./deploy.sh
```

The script will:
1. Pull latest changes from Git
2. Stop the systemd service
3. Build and publish the application
4. Restart the service
5. Verify it's running correctly

### Google Drive API Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Enable Google Drive API
3. Create credentials:
   - **Development**: OAuth 2.0
   - **Production**: Service Account

### Database Configuration

The application uses SQLite with a **single, consistent approach** across all environments:

### Connection String Priority (in order)
1. `ConnectionStrings__DefaultConnection` (appsettings.json or environment variable)
2. `CONNECTION_STRING` environment variable  
3. **Default**: `Data Source={AppContext.BaseDirectory}/jumpchain.db;Mode=ReadWrite`

### Environment Variable (Optional)
If you need the database in a specific location (e.g., `/var/lib/jumpchain/jumpsearch.db` in production), set **ONE** of these:

```bash
# Option 1: Full connection string (recommended for production)
CONNECTION_STRING="Data Source=/var/lib/jumpchain/jumpsearch.db;Mode=ReadWrite"

# Option 2: Just the path (app will build connection string)
JUMPCHAIN_DB_PATH="/var/lib/jumpchain/jumpsearch.db"
```

### Why This Approach?
- **No symlinks needed**: App reads from configured location directly
- **Same code path**: Dev and production use identical logic
- **Clear precedence**: Explicit config > environment variable > sensible default
- **Migration-friendly**: deploy.sh reads from systemd and applies migrations to correct database

## Environment Variables

Copy `.env.example` to `.env` and fill in your credentials.

### Database

- **SQLite**: Default for development, auto-creates on first run.
- **PostgreSQL**: Update connection string in `appsettings.Production.json` for production.

### Docker

```bash
docker build -t jumpchain-search .
docker run -d -p 80:8080 jumpchain-search
```

### Linux VPS (Ubuntu 24.04) - Production Deployment

**Prerequisites:**
- Ubuntu 24.04 LTS VPS
- .NET 8.0 SDK and runtime installed
- Nginx installed
- Domain name (optional, but recommended for SSL)

**Quick Deployment:**

1. **Clone and build:**
```bash
cd /home/deploy
git clone <repository-url> JumpChainSearch
cd JumpChainSearch
dotnet publish -c Release -o ./publish
```

2. **Setup directories and permissions:**
```bash
sudo mkdir -p /var/lib/jumpchain /etc/jumpchain
sudo chown deploy:www-data /var/lib/jumpchain
sudo chmod 775 /var/lib/jumpchain
```

3. **Copy service account key:**
```bash
sudo cp service-account.json /etc/jumpchain/
sudo chown root:www-data /etc/jumpchain/service-account.json
sudo chmod 640 /etc/jumpchain/service-account.json
```

4. **Transfer database (if migrating):**
```bash
# From local Windows machine:
pscp -i path\to\private-key.ppk jumpchain.db deploy@YOUR_VPS_IP:/tmp/
# On VPS:
sudo mv /tmp/jumpchain.db /var/lib/jumpchain/jumpsearch.db
sudo chown deploy:www-data /var/lib/jumpchain/jumpsearch.db
sudo chmod 664 /var/lib/jumpchain/jumpsearch.db
```

5. **Create systemd service:**
```bash
sudo nano /etc/systemd/system/jumpchain.service
```

Paste this configuration (update environment variables as needed):
```ini
[Unit]
Description=JumpChain Search Application
After=network.target

[Service]
Type=simple
User=deploy
Group=www-data
WorkingDirectory=/home/deploy/JumpChainSearch/publish
ExecStart=/usr/bin/dotnet JumpChainSearch.dll
Restart=on-failure
RestartSec=10
Environment="CONNECTION_STRING=Data Source=/var/lib/jumpchain/jumpsearch.db;Mode=ReadWrite"
Environment="GOOGLE_API_KEY=YOUR_API_KEY_HERE"
Environment="GOOGLE_APPLICATION_CREDENTIALS=/etc/jumpchain/service-account.json"
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="ASPNETCORE_URLS=http://0.0.0.0:5248"
Environment="JUMPCHAIN_DRIVES_CONFIG=[{\"name\":\"Drive1\",\"folderId\":\"ID1\"},{\"name\":\"Drive2\",\"folderId\":\"ID2\"}]"
LimitNOFILE=10000

[Install]
WantedBy=multi-user.target
```

**Important:** The directory `/var/lib/jumpchain/` must be owned by the `deploy` user so SQLite can create WAL/SHM lock files. If you get "readonly database" errors, check directory ownership with `ls -la /var/lib/jumpchain/`.

6. **Start the service:**
```bash
sudo systemctl daemon-reload
sudo systemctl enable jumpchain
sudo systemctl start jumpchain
sudo systemctl status jumpchain
```

7. **Configure Nginx reverse proxy:**
```bash
sudo nano /etc/nginx/sites-available/jumpchain
```

Paste:
```nginx
server {
    listen 80;
    server_name YOUR_DOMAIN_OR_IP;

    location / {
        proxy_pass http://localhost:5248;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_buffering off;
    }
}
```

Enable and reload:
```bash
sudo ln -s /etc/nginx/sites-available/jumpchain /etc/nginx/sites-enabled/
sudo rm /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl reload nginx
```

8. **Optional - SSL with Let's Encrypt:**
```bash
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d yourdomain.com
```

**Deploying Updates:**

After initial setup, use the deployment script to update:

```bash
cd /home/deploy/JumpChainSearch
chmod +x deploy.sh  # Only needed once
./deploy.sh
```

The script automatically pulls changes, rebuilds, and restarts the service.

See `.github/reference/DEPLOYMENT_GUIDE.md` for troubleshooting and advanced configuration.

---

## Performance & Architecture Notes

- **Document Count Service**: In-memory, thread-safe, zero-DB queries per page load, auto-refresh, singleton pattern.
- **Caching**: Redis recommended for production.
- **Background Jobs**: Hangfire for scheduled scans.
- **File Storage**: Extracted text stored separately for large docs.

---

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## License

MIT License. See LICENSE file for details.

## Support

- [Open an issue on GitHub](https://github.com/ZachHunt359/JumpChainSearch/issues)
- Check `.github/reference/` for technical docs
- Join the JumpChain community discussions