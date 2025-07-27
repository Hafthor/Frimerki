# Email Server Specification

## Overview

This document outlines the specification for a lightweight, self-contained email server built in C#. The server is designed to run on minimal hardware with few dependencies, using SQLite as the primary data store.

## Architecture

### High-Level Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Email Clients │    │   Web Interface │    │  Admin Interface│
│   (POP3/IMAP)   │    │      (SPA)      │    │      (SPA)      │
└─────────┬───────┘    └─────────┬───────┘    └─────────┬───────┘
          │                      │                      │
          │              ┌───────┴──────────────────────┴───────┐
          │              │           Web API Layer              │
          │              └───────┬──────────────────────────────┘
          │                      │
    ┌─────┴──────────────────────┴─────┐
    │        Email Server Core         │
    │  ┌─────────┐ ┌─────────┐ ┌──────┐│
    │  │  SMTP   │ │  IMAP   │ │ POP3 ││
    │  │ Service │ │ Service │ │Service││
    │  └─────────┘ └─────────┘ └──────┘│
    └─────┬────────────────────────────┘
          │
    ┌─────┴─────┐
    │  SQLite   │
    │ Database  │
    └───────────┘
```

### Core Components

1. **Email Protocol Services**
   - SMTP Service (Port 25/587/465)
   - IMAP Service (Port 143/993)
   - POP3 Service (Port 110/995)

2. **Web API Layer**
   - RESTful API for email management
   - Authentication and authorization
   - Real-time notifications (SignalR)

3. **Data Layer**
   - SQLite database
   - Entity Framework Core
   - Repository pattern

4. **Security Layer**
   - SSL/TLS encryption
   - User authentication
   - Rate limiting
   - Spam filtering

5. **Configuration Management**
   - Domain management
   - User account management
   - Security settings

## Database Schema

### Core Tables

#### Users
```sql
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique user identifier
    Username TEXT NOT NULL,                                  -- Username part of email address (before @)
    DomainId INTEGER NOT NULL,                               -- Foreign key to Domains table
    PasswordHash TEXT NOT NULL,                              -- BCrypt hashed password
    Salt TEXT NOT NULL,                                      -- Salt used for password hashing
    FullName TEXT,                                           -- User's display name
    CanReceive BOOLEAN DEFAULT 1,                            -- Whether user can receive emails
    CanLogin BOOLEAN DEFAULT 1,                              -- Whether user can log in to services
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- Account creation timestamp
    LastLogin DATETIME,                                      -- Last successful login timestamp
    FOREIGN KEY (DomainId) REFERENCES Domains(Id),
    UNIQUE(Username, DomainId)                               -- Unique username per domain
);
```

#### Domains
```sql
CREATE TABLE Domains (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique domain identifier
    Name TEXT NOT NULL UNIQUE,                               -- Domain name (e.g., example.com)
    IsActive BOOLEAN DEFAULT 1,                              -- Whether domain accepts mail
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP             -- Domain creation timestamp
);
```

#### Messages
```sql
CREATE TABLE Messages (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique message identifier
    HeaderMessageId TEXT NOT NULL UNIQUE,                    -- RFC 2822 Message-ID header
    FromAddress TEXT NOT NULL,                               -- Sender's email address
    Subject TEXT,                                            -- Message subject line
    Headers TEXT,                                            -- Raw email headers (To/Cc/Bcc/etc)
    Body TEXT,                                               -- Plain text message body
    BodyHtml TEXT,                                           -- HTML message body
    MessageSize INTEGER,                                     -- Message size in bytes
    ReceivedAt DATETIME DEFAULT CURRENT_TIMESTAMP,           -- When message was received
    Flags TEXT,                                              -- IMAP flags (JSON or delimited)
    FolderId INTEGER,                                        -- Default folder for message
    FOREIGN KEY (FolderId) REFERENCES Folders(Id)
);
```

#### Folders
```sql
CREATE TABLE Folders (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique folder identifier
    UserId INTEGER NOT NULL,                                 -- Owner of this folder
    Name TEXT NOT NULL,                                      -- Full folder path (e.g., INBOX/Work)
    SystemFolderType TEXT DEFAULT NULL,                      -- System folder type or NULL for user folders
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- Folder creation timestamp
    FOREIGN KEY (UserId) REFERENCES Users(Id),
    UNIQUE(UserId, Name),                                    -- Unique folder name per user
    UNIQUE(UserId, SystemFolderType) WHERE SystemFolderType IS NOT NULL  -- One system folder per type per user
);
```

**SystemFolderType Enum Values:**
- `NULL` - User-created folder
- `"INBOX"` - Primary inbox for incoming messages
- `"SENT"` - Sent messages folder
- `"DRAFTS"` - Draft messages folder
- `"TRASH"` - Deleted messages folder
- `"SPAM"` - Spam/Junk messages folder
- `"OUTBOX"` - Messages queued for sending

#### UserMessages
```sql
CREATE TABLE UserMessages (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique user-message relationship identifier
    UserId INTEGER NOT NULL,                                 -- User who owns this message instance
    MessageId INTEGER NOT NULL,                              -- Reference to the actual message
    FolderId INTEGER NOT NULL,                               -- Which folder this message is in for this user
    IsRead BOOLEAN DEFAULT 0,                                -- Whether this user has read the message
    IsStarred BOOLEAN DEFAULT 0,                             -- Whether this user has starred the message
    IsDeleted BOOLEAN DEFAULT 0,                             -- Whether this user has deleted the message
    ReceivedAt DATETIME DEFAULT CURRENT_TIMESTAMP,           -- When this user received the message
    FOREIGN KEY (UserId) REFERENCES Users(Id),
    FOREIGN KEY (MessageId) REFERENCES Messages(Id),
    FOREIGN KEY (FolderId) REFERENCES Folders(Id)
);
```

#### Attachments
```sql
CREATE TABLE Attachments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique attachment identifier
    MessageId INTEGER NOT NULL,                              -- Message this attachment belongs to
    FileName TEXT NOT NULL,                                  -- Original filename of attachment
    ContentType TEXT,                                        -- MIME type (e.g., image/jpeg)
    Size INTEGER,                                            -- File size in bytes
    FileGuid TEXT NOT NULL UNIQUE,                           -- GUID used as filename on disk
    FilePath TEXT,                                           -- Full path to stored file (includes /attachments/guid)
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- When attachment was stored
    FOREIGN KEY (MessageId) REFERENCES Messages(Id)
);
```

## Protocol Support

### SMTP (Simple Mail Transfer Protocol)
- **Port 25**: Standard SMTP (unencrypted)
- **Port 587**: SMTP with STARTTLS
- **Port 465**: SMTP over SSL/TLS
- **Features**:
  - Message routing and delivery
  - Authentication (AUTH LOGIN, AUTH PLAIN)
  - STARTTLS support
  - Message size limits
  - Rate limiting
  - Basic spam filtering

### IMAP (Internet Message Access Protocol)
- **Port 143**: Standard IMAP (unencrypted)
- **Port 993**: IMAP over SSL/TLS
- **Features**:
  - Folder hierarchy support
  - Message flags and status
  - Server-side search
  - Partial message fetching
  - IDLE command for real-time updates
  - Multi-folder synchronization

### POP3 (Post Office Protocol)
- **Port 110**: Standard POP3 (unencrypted)
- **Port 995**: POP3 over SSL/TLS
- **Features**:
  - Download and delete model
  - Leave messages on server option
  - APOP authentication
  - Basic message retrieval

## Attachment Storage Strategy

### File Storage Design
- **Storage Location**: `/attachments` directory (configurable)
- **File Naming**: GUID-based filenames to prevent conflicts and directory traversal
- **Static Serving**: Files served directly by web server for performance
- **URL Format**: `/attachments/{guid}` (relative path)

### Security Considerations
- **Initial Implementation**: Static file serving for simplicity
- **Future Enhancement**: Authentication-protected streaming via API endpoint
- **File Access**: Attachment path included in message response for client-side URL construction
- **GUID Benefits**: 
  - Prevents filename conflicts
  - Obscures original filenames
  - Makes file enumeration difficult
  - Enables easy cleanup of orphaned files

### Example Attachment Response
```json
{
  "id": 123,
  "subject": "Document with attachments",
  "attachments": [
    {
      "id": 1,
      "fileName": "document.pdf",
      "contentType": "application/pdf",
      "size": 1024000,
      "path": "/attachments/550e8400-e29b-41d4-a716-446655440000"
    }
  ]
}
```

## Web API Endpoints

### Authentication Endpoints
```
POST   /api/session             - Create session (login)
DELETE /api/session             - Delete session (logout)
GET    /api/session             - Get current session/user info (auto-refreshes token)
```

### User Management
```
GET    /api/users                    - List all users (admin)
POST   /api/users                    - Create new user (admin)
GET    /api/users/{email}            - Get user details
PUT    /api/users/{email}            - Update user (full replacement)
PATCH  /api/users/{email}            - Partial update user (including password)
DELETE /api/users/{email}            - Delete user (admin)
```

### Message Management
```
GET    /api/messages            - List messages with pagination/filtering
GET    /api/messages/{id}       - Get specific message (includes attachment metadata with paths)
POST   /api/messages            - Send new message
PUT    /api/messages/{id}       - Update message (flags, folder)
DELETE /api/messages/{id}       - Delete message
```

### Folder Management
```
GET    /api/folders             - List user folders
POST   /api/folders             - Create new folder
PUT    /api/folders/{id}        - Update folder
DELETE /api/folders/{id}        - Delete folder
POST   /api/folders/{id}/messages/{messageId} - Move message to folder
```

### Domain Management (Admin)
```
GET    /api/domains             - List domains
POST   /api/domains             - Add new domain
PUT    /api/domains/{domainname} - Update domain
DELETE /api/domains/{domainname} - Delete domain
```

### Server Management (Admin)
```
GET    /api/server/status       - Server status and statistics
GET    /api/server/logs         - Server logs
PUT    /api/server/settings     - Update server settings
POST   /api/server/backup       - Create backup
POST   /api/server/restore      - Restore from backup
```

### Real-time Endpoints (SignalR)
```
/hubs/email                     - Real-time email notifications
  - NewMessage(messageInfo)
  - MessageRead(messageId)
  - MessageDeleted(messageId)
  - FolderUpdated(folderInfo)
```

## Security Features

### Encryption
- **SSL/TLS Support**: All protocols support encrypted connections
- **Certificate Management**: Automatic certificate generation and renewal
- **Perfect Forward Secrecy**: Modern cipher suites

### Authentication
- **Password Hashing**: BCrypt with salt
- **JWT Tokens**: For web API authentication
- **Session Management**: Secure session handling
- **Multi-factor Authentication**: TOTP support (future enhancement)

### Authorization
- **Role-based Access**: Admin, User roles
- **Resource-based Permissions**: Users can only access their own emails
- **API Rate Limiting**: Prevent abuse

### Security Measures
- **Input Validation**: Comprehensive input sanitization
- **SQL Injection Prevention**: Parameterized queries
- **XSS Protection**: Output encoding
- **CSRF Protection**: Anti-forgery tokens
- **Brute Force Protection**: Account lockout after failed attempts

## Configuration

### Server Configuration
```json
{
  "Server": {
    "MaxMessageSize": "25MB",
    "StorageQuotaPerUser": "1GB",
    "AttachmentsPath": "/attachments",
    "EnableSMTP": true,
    "EnableIMAP": true,
    "EnablePOP3": true,
    "EnableWebAPI": true
  },
  "Ports": {
    "SMTP": 25,
    "SMTP_TLS": 587,
    "SMTP_SSL": 465,
    "IMAP": 143,
    "IMAP_SSL": 993,
    "POP3": 110,
    "POP3_SSL": 995,
    "WebAPI": 8080,
    "WebAPI_SSL": 8443
  },
  "Database": {
    "ConnectionString": "Data Source=emailserver.db",
    "BackupInterval": "24h"
  },
  "Security": {
    "RequireSSL": true,
    "JWTSecret": "auto-generated",
    "CertificatePath": "/certs/",
    "EnableRateLimit": true,
    "MaxFailedLogins": 5
  }
}
```

## Technology Stack

### Backend
- **.NET 8**: Latest LTS version
- **ASP.NET Core**: Web API framework
- **Entity Framework Core**: ORM for SQLite
- **SignalR**: Real-time communication
- **Serilog**: Structured logging
- **FluentValidation**: Input validation
- **AutoMapper**: Object mapping

### Email Libraries
- **MailKit**: IMAP/POP3/SMTP implementation
- **MimeKit**: MIME message parsing
- **Custom Protocol Handlers**: For fine-tuned control

### Security Libraries
- **BCrypt.NET**: Password hashing
- **System.IdentityModel.Tokens.Jwt**: JWT token handling
- **BouncyCastle**: Cryptographic operations

### Database
- **SQLite**: Embedded database
- **SQLite.Interop**: Native SQLite library

## Deployment Requirements

### Minimum Hardware
- **CPU**: 1-2 cores
- **RAM**: 2GB minimum, 4GB recommended
- **Storage**: 10GB minimum for system, additional for email storage
- **Network**: Stable internet connection with static IP

### Software Requirements
- **.NET 8 Runtime**
- **Operating System**: Windows, Linux, or macOS
- **Reverse Proxy**: Nginx or Apache (recommended for production)

### DNS Configuration
```
A     mail.example.com      → server_ip
MX    example.com          → mail.example.com (priority 10)
TXT   example.com          → "v=spf1 ip4:server_ip ~all"
TXT   _dmarc.example.com   → "v=DMARC1; p=quarantine;"
```

## Development Phases

### Phase 1: Core Infrastructure
1. Project setup and configuration
2. Database schema and Entity Framework setup
3. Basic authentication and user management
4. Logging and monitoring

### Phase 2: Email Protocols
1. SMTP server implementation
2. IMAP server implementation
3. POP3 server implementation
4. Protocol testing and validation

### Phase 3: Web API
1. RESTful API development
2. JWT authentication
3. Real-time notifications with SignalR
4. API documentation

### Phase 4: Security & Features
1. SSL/TLS implementation
2. Rate limiting and spam filtering
3. Admin management interface
4. Backup and restore functionality

### Phase 5: Frontend Applications
1. User email interface (SPA)
2. Admin management interface
3. Mobile-responsive design
4. Progressive Web App features

## Testing Strategy

### Unit Testing
- Service layer testing
- Repository pattern testing
- Validation testing
- Security feature testing

### Integration Testing
- Database integration
- Email protocol testing
- API endpoint testing
- Authentication flow testing

### Performance Testing
- Load testing for concurrent connections
- Message throughput testing
- Database performance testing
- Memory usage optimization

## Monitoring and Maintenance

### Logging
- Structured logging with Serilog
- Error tracking and alerting
- Performance metrics
- Security event logging

### Backup Strategy
- Automated daily database backups
- Configuration backup
- Email storage backup
- Restore procedures

### Maintenance Tasks
- Log rotation
- Database optimization
- Certificate renewal
- Security updates

## Future Enhancements

### Advanced Features
- Multi-tenancy support
- Advanced spam filtering with machine learning
- Email encryption (PGP/S-MIME)
- Calendar and contacts integration
- Mobile push notifications

### Scalability Options
- Database clustering (SQLite → PostgreSQL/MySQL)
- Load balancing
- Microservices architecture
- Docker containerization
- Kubernetes deployment

---

This specification provides a comprehensive foundation for building a modern, lightweight email server in C# with minimal dependencies and hardware requirements.
