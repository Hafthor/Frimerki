# Frímerki Email Server

**Frímerki** (Icelandic for "postage stamp", pronounced [ˈfrɪːmɛr̥kɪ] or FREE-mer-kih) is a lightweight, self-contained email server built with C# and .NET 8. Designed for minimal hardware requirements and maximum efficiency.

## 🚀 Quick Start

### Prerequisites
- .NET 8 SDK
- Visual Studio Code, Visual Studio or JetBrains Rider

### Running the Server

1. **Clone and navigate to the project:**
   ```bash
   git clone https://github.com/Hafthor/Frimerki.git
   cd Frimerki
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

### Testing Email Functionality

1. **Create a domain and user via API:**
   ```bash
   # Create a domain
   curl -X POST http://localhost:5000/api/domains \
     -H "Content-Type: application/json" \
     -d '{"name": "example.com"}'

   # Create a user
   curl -X POST http://localhost:5000/api/users \
     -H "Content-Type: application/json" \
     -d '{"email": "user@example.com", "password": "secure123", "domainId": 1}'
   ```

2. **Configure your email client:**
   - **SMTP Server**: localhost:25 (or 587)
   - **IMAP Server**: localhost:143 (or 993)
   - **POP3 Server**: localhost:110 (or 995)
   - **Username**: user@example.com
   - **Password**: secure123

3. **Send a test email:**
   ```bash
   # Use any SMTP client or the built-in functionality
   # Emails sent to user@example.com will appear in their INBOX
   ```

## 🏗️ Project Structure

```
Frimerki/
├── src/
│   ├── Frimerki.Server/      # Main web server (ASP.NET Core)
│   ├── Frimerki.Data/        # Entity Framework & Database
│   ├── Frimerki.Models/      # Data models & entities
│   ├── Frimerki.Services/    # Business logic & services
│   └── Frimerki.Protocols/   # SMTP, IMAP & POP3 protocol implementations
├── tests/
│   └── Frimerki.Tests/       # Comprehensive test suite (113 tests)
├── Frimerki.sln             # Solution file
└── SPECIFICATION.md         # Full technical specification
```

## 📧 Email Protocol Support

- **SMTP Server**: ✅ Complete receive mail functionality with MIME parsing
- **IMAP Server**: ✅ Full implementation with mailbox management
- **POP3 Server**: ✅ Complete RFC 1939 compliance with message retrieval and deletion

### SMTP Features
- Email reception and delivery to user mailboxes
- MIME message parsing and storage
- Multi-recipient support with proper validation
- Thread-safe concurrent email processing

### IMAP Features
- Complete RFC 3501 compliance
- Mailbox management (CREATE, DELETE, SELECT, EXAMINE)
- Message operations (FETCH, STORE, SEARCH, UID commands)
- **Complete flag operations**: STORE command with +FLAGS, -FLAGS, FLAGS, and SILENT support
- **Message deletion**: EXPUNGE command for permanent removal of deleted messages
- Flag management (SEEN, ANSWERED, FLAGGED, DELETED, DRAFT)
- Real-time folder synchronization

### POP3 Features
- RFC 1939 compliance with extended capabilities
- User authentication with username/password
- Message listing and retrieval (LIST, RETR, STAT)
- Message deletion with DELE and QUIT commands
- Unique identifier support (UIDL)
- Top command for header retrieval (TOP)
- Extended capabilities advertising (CAPA)

## 🛠️ Technology Stack

- **.NET 8** - Modern, cross-platform framework with C# 12 features
- **ASP.NET Core** - Web API with JWT authentication
- **Entity Framework Core** - Database ORM with SQLite
- **SQLite** - Embedded database with in-memory testing
- **MimeKit** - Email MIME parsing and generation
- **Serilog** - Structured logging

## 🔧 Development Commands

```bash
# Build the solution
dotnet build

# Run all tests (113 tests)
dotnet test

# Run tests with detailed output
dotnet test --logger console --verbosity minimal

# Run the server in development mode
dotnet run --project src/Frimerki.Server --environment Development

# Run specific test categories
dotnet test --filter "Category=SMTP"
dotnet test --filter "Category=IMAP"
dotnet test --filter "Pop3"

# Watch mode for continuous testing during development
dotnet watch test
```

## 📋 Current Status

**Phase 1: Core Infrastructure** ✅
- [x] Project structure and dependencies
- [x] Web server with comprehensive API
- [x] Entity Framework models and DbContext
- [x] Configuration system with environment support
- [x] Structured logging with Serilog
- [x] Health check endpoints
- [x] Comprehensive test suite (113 tests)

**Phase 2: Email Protocols** ✅
- [x] SMTP server with receive mail functionality
- [x] IMAP server with full RFC 3501 compliance
- [x] Complete IMAP flag operations (STORE and EXPUNGE commands)
- [x] POP3 server with complete RFC 1939 compliance
- [x] MIME message parsing and storage
- [x] Thread-safe concurrent processing

**Phase 3: Web API** ✅
- [x] JWT authentication with refresh tokens
- [x] User management with BCrypt password hashing
- [x] Domain management and validation
- [x] Message management with filtering and search
- [x] Folder management (INBOX, SENT, custom folders)
- [x] Real-time message flag synchronization

**Phase 4: Performance & Quality** ✅
- [x] Source-generated regex for compile-time optimization
- [x] Immutable collections (FrozenDictionary) for performance
- [x] Thread-safe concurrent collections
- [x] Modern C# patterns (switch expressions, pattern matching)
- [x] Comprehensive error handling and validation

## 🎯 Features

### 📬 Email Server Capabilities
- **Receive Emails**: Full SMTP server receives and processes incoming emails
- **Mailbox Management**: Complete IMAP server for email client compatibility
- **Mail Retrieval**: Full POP3 server for traditional email client support
- **Multi-Domain Support**: Host multiple email domains on one server
- **User Management**: Create and manage email accounts with secure authentication
- **Message Storage**: Efficient SQLite database with proper indexing
- **Concurrent Processing**: Thread-safe operations for high-performance email handling

### 🔐 Security & Authentication
- **JWT Tokens**: Secure API authentication with refresh token support
- **BCrypt Password Hashing**: Industry-standard password security
- **Input Validation**: Comprehensive validation with source-generated regex
- **CSRF Protection**: Built-in security features

### 🏗️ Developer Experience
- **Modern C# Patterns**: Switch expressions, pattern matching, record types
- **Performance Optimized**: Source-generated regex, immutable collections
- **Comprehensive Tests**: 113 tests covering all functionality
- **Clean Architecture**: Separation of concerns with clear service boundaries
- **API Documentation**: Swagger/OpenAPI integration

## 📖 Documentation

- [Full Specification](SPECIFICATION.md) - Complete technical specification
- [API Documentation](http://localhost:5000/swagger) - Interactive API docs (when running)

## 🌟 Why Frímerki?

- **Production Ready**: Complete email server with SMTP and IMAP support
- **Lightweight**: Minimal dependencies, runs on modest hardware
- **Self-contained**: Single binary deployment with embedded SQLite database
- **Modern**: Built with .NET 8 and latest C# 12 features
- **Fast**: Performance-optimized with source-generated regex and immutable collections
- **Reliable**: Comprehensive test suite ensures stability and correctness
- **Compliant**: Full RFC compliance for email protocols
- **Extensible**: Clean architecture ready for future enhancements

---

**Etymology**: *Frímerki* is the Icelandic word for "postage stamp" - small, essential, and designed to deliver messages reliably across any distance.
