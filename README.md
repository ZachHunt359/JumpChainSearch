# JumpChain Search

A web application for searching through JumpChain documents across multiple Google Drives. Built with ASP.NET Core Blazor Server and designed for the JumpChain community.

## Features

- **Multi-Drive Search**: Search across multiple Google Drives simultaneously
- **Smart Tagging**: Automatic tagging based on drive, folder structure, and document content
- **Full-Text Search**: Search document contents (Google Docs, PDFs, Word documents)
- **Advanced Filtering**: Filter by drive, tags, document type, and more
- **Real-time Results**: Fast, responsive search interface
- **Mobile Friendly**: Works on desktop, tablet, and mobile devices

## Technology Stack

- **Backend**: ASP.NET Core 8.0 with Blazor Server
- **Database**: SQLite (development) / PostgreSQL (production)
- **ORM**: Entity Framework Core
- **API Integration**: Google Drive API v3
- **Frontend**: Blazor Server Components with Bootstrap 5
- **Deployment**: Cross-platform (Windows, Linux, macOS)

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Google Cloud Project with Drive API enabled
- Service Account credentials (for production) or OAuth2 credentials (for development)

## Setup Instructions

### 1. Clone and Build

```bash
git clone <repository-url>
cd JumpChainSearch
dotnet restore
dotnet build
```

### 2. Google Drive API Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select an existing one
3. Enable the Google Drive API
4. Create credentials:
   - For **development**: Create OAuth 2.0 credentials
   - For **production**: Create a Service Account

### 3. Configure Environment Variables

Copy `.env.example` to `.env` and update with your values:

```bash
cp .env .env.local
```

Edit `.env.local` with your Google Drive API credentials.

### 4. Database Setup

The application will automatically create the SQLite database on first run:

```bash
dotnet ef database update
```

### 5. Run the Application

```bash
dotnet run
```

The application will be available at `https://localhost:5001`

## Configuration

### Google Drive API

The application supports two authentication methods:

#### Service Account (Recommended for Production)
- More secure for server applications
- Requires service account JSON key
- Can access shared drives if properly configured

#### OAuth2 (Development)
- Requires user consent
- Good for development and testing
- Limited to user's accessible drives

### Database

#### Development (SQLite)
- Default configuration
- No additional setup required
- Database file: `jumpchain.db`

#### Production (PostgreSQL)
Update connection string in `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=jumpchain;Username=user;Password=password"
  }
}
```

## Docker Deployment

### Build Docker Image

```bash
docker build -t jumpchain-search .
```

### Run with Docker

```bash
docker run -d \
  --name jumpchain-search \
  -p 80:8080 \
  -e GoogleDrive__ServiceAccountKey="$(cat service-account.json)" \
  -v jumpchain-data:/app/data \
  jumpchain-search
```

## Linux VPS Deployment (Ubuntu 24.04)

### 1. Install .NET Runtime

```bash
sudo apt update
sudo apt install -y aspnetcore-runtime-8.0
```

### 2. Create Application User

```bash
sudo useradd -m -s /bin/bash jumpchain
sudo usermod -aG www-data jumpchain
```

### 3. Deploy Application

```bash
# Publish the application
dotnet publish -c Release -o ./publish

# Copy to server
scp -r ./publish/* user@your-server:/var/www/jumpchain/

# Set permissions
sudo chown -R jumpchain:www-data /var/www/jumpchain
sudo chmod -R 755 /var/www/jumpchain
```

### 4. Configure Systemd Service

Create `/etc/systemd/system/jumpchain.service`:

```ini
[Unit]
Description=JumpChain Search Application
After=network.target

[Service]
Type=notify
User=jumpchain
Group=www-data
WorkingDirectory=/var/www/jumpchain
ExecStart=/usr/bin/dotnet JumpChainSearch.dll
Restart=on-failure
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
```

### 5. Configure Nginx Reverse Proxy

Create `/etc/nginx/sites-available/jumpchain`:

```nginx
server {
    listen 80;
    server_name your-domain.com;
    
    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### 6. Start Services

```bash
sudo systemctl enable jumpchain
sudo systemctl start jumpchain
sudo systemctl enable nginx
sudo systemctl restart nginx
```

## Development

### Project Structure

```
JumpChainSearch/
├── Data/                  # Entity Framework DbContext
├── Models/               # Data models
├── Pages/                # Blazor pages
├── Services/             # Business logic services
├── wwwroot/              # Static files
├── Program.cs            # Application startup
└── appsettings.json      # Configuration
```

### Adding New Features

1. **Models**: Add new entities to `Models/` directory
2. **Services**: Add business logic to `Services/` directory
3. **Pages**: Add new Blazor pages to `Pages/` directory
4. **Database**: Update DbContext and run migrations

### Running Tests

```bash
dotnet test
```

## API Endpoints

The application includes the following key components:

- **Search Service**: Handles document search and filtering
- **Google Drive Service**: Manages Google Drive API integration
- **Document Scanner**: Scans drives and extracts document metadata

## Performance Considerations

- **Caching**: Implement Redis for production caching
- **Background Jobs**: Use Hangfire for scheduled drive scans
- **Database**: Consider PostgreSQL for production workloads
- **File Storage**: Store extracted text separately for large documents

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

For issues and questions:
- Create an issue on GitHub
- Check the documentation
- Join the JumpChain community discussions