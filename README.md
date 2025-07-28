# Frímerki Email Server

**Frímerki** (Icelandic for "postage stamp") is a lightweight, self-contained email server built with C# and .NET 8. Designed for minimal hardware requirements and maximum efficiency.

## 🚀 Quick Start

### Prerequisites
- .NET 8 SDK
- Visual Studio Code or Visual Studio

### Running the Server

1. **Clone and navigate to the project:**
   ```bash
   cd /Users/hafthor/Desktop/Frimerki
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

3. **Run the server:**
   ```bash
   dotnet run --project src/Frimerki.Server
   ```

4. **Open your browser:**
   - Main interface: http://localhost:5000
   - API documentation: http://localhost:5000/swagger
   - Health check: http://localhost:5000/api/health

## 🏗️ Project Structure

```
Frimerki/
├── src/
│   ├── Frimerki.Server/      # Main web server (ASP.NET Core)
│   ├── Frimerki.Data/        # Entity Framework & Database
│   ├── Frimerki.Models/      # Data models & entities
│   └── Frimerki.Services/    # Business logic & services
├── tests/
│   └── Frimerki.Tests/       # Unit tests
├── Frimerki.sln             # Solution file
└── specification.md         # Full technical specification
```

## 📧 Email Protocol Support

- **SMTP**: Ports 25, 587, 465 (sending/receiving)
- **IMAP**: Ports 143, 993 (mailbox access)
- **POP3**: Ports 110, 995 (simple mail retrieval)

## 🛠️ Technology Stack

- **.NET 8** - Modern, cross-platform framework
- **ASP.NET Core** - Web API and real-time features
- **Entity Framework Core** - Database ORM
- **SQLite** - Embedded database with FTS5 search
- **SignalR** - Real-time notifications
- **MailKit/MimeKit** - Email protocol implementation
- **Serilog** - Structured logging

## 🔧 Development Commands

```bash
# Build the solution
dotnet build

# Run tests
dotnet test

# Run the server in development mode
dotnet run --project src/Frimerki.Server --environment Development

# Create database migration (when ready)
dotnet ef migrations add InitialCreate --project src/Frimerki.Data --startup-project src/Frimerki.Server

# Update database
dotnet ef database update --project src/Frimerki.Data --startup-project src/Frimerki.Server
```

## 📋 Current Status

**Phase 1: Core Infrastructure** ✅
- [x] Project structure and dependencies
- [x] Basic web server with hello world page
- [x] Entity Framework models and DbContext
- [x] Configuration system
- [x] Logging infrastructure
- [x] Health check endpoints

**Phase 2: Email Protocols** 🚧
- [ ] SMTP server implementation
- [ ] IMAP server implementation
- [ ] POP3 server implementation

**Phase 3: Web API** ⏳
- [ ] Authentication & JWT tokens
- [ ] Message management endpoints
- [ ] User management
- [ ] Real-time notifications

## 🎯 Next Steps

1. **Database Migrations**: Set up Entity Framework migrations
2. **SMTP Implementation**: Start with basic SMTP message reception
3. **Authentication**: Implement JWT-based authentication
4. **IMAP Basic Support**: Core IMAP commands for Outlook/Apple Mail

## 📖 Documentation

- [Full Specification](specification.md) - Complete technical specification
- [API Documentation](http://localhost:5000/swagger) - Interactive API docs (when running)

## 🌟 Why Frímerki?

- **Lightweight**: Minimal dependencies, runs on modest hardware
- **Self-contained**: Single binary deployment with embedded database
- **Modern**: Built with .NET 8 and modern C# features
- **Compliant**: Full RFC compliance for email protocols
- **Extensible**: Clean architecture for future enhancements

---

**Etymology**: *Frímerki* is the Icelandic word for "postage stamp" - small, essential, and designed to deliver messages reliably across any distance.
