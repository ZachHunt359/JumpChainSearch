  --name jumpchain-search \
  -p 80:8080 \
  -e GoogleDrive__ServiceAccountKey="$(cat service-account.json)" \
  -v jumpchain-data:/app/data \
  jumpchain-search

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

### Google Drive API Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Enable Google Drive API
3. Create credentials:
   - **Development**: OAuth 2.0
   - **Production**: Service Account

### Environment Variables

Copy `.env.example` to `.env` and fill in your credentials.

### Database

- **SQLite**: Default for development, auto-creates on first run.
- **PostgreSQL**: Update connection string in `appsettings.Production.json` for production.

### Docker

```bash
docker build -t jumpchain-search .
docker run -d -p 80:8080 jumpchain-search
```

### Linux VPS (Ubuntu 24.04)

See `.github/reference/DEPLOYMENT_GUIDE.md` for full instructions.

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