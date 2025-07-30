# Frímerki Email Server Specification

## Overview

This document outlines the specification for Frímerki, a lightweight, self-contained email server built in C#. The server is designed to run on minimal hardware with few dependencies, using SQLite as the primary data store.

## Architecture

### High-Level Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│  Email Clients  │    │  Web Interface  │    │ Admin Interface │
│   (POP3/IMAP)   │    │      (SPA)      │    │      (SPA)      │
└─────────┬───────┘    └─────────┬───────┘    └─────────┬───────┘
          │                      │                      │
          │              ┌───────┴──────────────────────┴───────┐
          │              │           Web API Layer              │
          │              └───────┬──────────────────────────────┘
          │                      │
    ┌─────┴──────────────────────┴───────┐
    │        Frímerki Server Core        │
    │  ┌─────────┐ ┌─────────┐ ┌───────┐ │
    │  │  SMTP   │ │  IMAP   │ │ POP3  │ │
    │  │ Service │ │ Service │ │Service│ │
    │  └─────────┘ └─────────┘ └───────┘ │
    └─────┬──────────────────────────────┘
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
   - Multi-database architecture with SQLite
   - Global database for server-wide settings (domain registry)
   - Domain-specific databases for email data and user management
   - Entity Framework Core with separate contexts
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

## Database Architecture

Frímerki uses a **multi-database architecture** to efficiently manage server-wide settings and domain-specific data:

### Global Database (`frimerki_global.db`)
Contains server-wide configuration and domain registry:

#### DomainRegistry
```sql
CREATE TABLE DomainRegistry (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique domain identifier
    Name TEXT NOT NULL UNIQUE,                               -- Domain name (e.g., example.com)
    DatabaseName TEXT NOT NULL,                              -- Database filename for this domain (e.g., 'domain_example_com' or 'shared_small_domains')
    IsActive BOOLEAN DEFAULT 1,                              -- Whether domain accepts mail (HostAdmin control)
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- Domain creation timestamp
);
```

**Database Naming Strategy:**
- **Default**: Each domain gets its own database (e.g., `frimerki_domain_example_com.db`)
- **Shared**: Multiple domains can share a database (e.g., `frimerki_shared_small_domains.db`)
- **Custom**: Flexible naming for specific organizational needs

**Registry Caching:**
- In-memory cache of domain-to-database mappings for performance
- Cache invalidation when DomainRegistry is modified
- Fallback to direct database lookup if cache miss occurs

#### Users (Global HostAdmin Accounts)
```sql
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique user identifier
    Username TEXT NOT NULL UNIQUE,                           -- Username for HostAdmin accounts
    Email TEXT NOT NULL UNIQUE,                              -- Full email (username@frimerki.local)
    PasswordHash TEXT NOT NULL,                              -- BCrypt hashed password
    Salt TEXT NOT NULL,                                      -- Salt used for password hashing
    FullName TEXT,                                           -- User's display name
    Role TEXT NOT NULL DEFAULT 'HostAdmin',                  -- Always 'HostAdmin' in global database
    CanLogin BOOLEAN DEFAULT 1,                              -- Whether user can log in
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- Account creation timestamp
    LastLogin DATETIME                                       -- Last successful login timestamp
);
```

**Note**: Global Users table is specifically for HostAdmin accounts with emails like `admin@frimerki.local`. Domain-specific user accounts are stored in their respective domain databases.

### Domain-Specific Databases (`frimerki_domain_{database_name}.db`)
Each domain references a database containing users, messages, and settings. Multiple domains can share the same database, or each domain can have its own dedicated database:

### Architecture Benefits
- **Separation of Concerns**: Server-level operations (domain activation) separate from domain-specific data
- **Administrative Isolation**: HostAdmin accounts stored globally (e.g., `admin@frimerki.local`), domain users stored per-domain
- **Flexible Database Grouping**: Domains can share databases for efficiency or have dedicated databases for isolation
- **Scalability**: Domain data can be distributed across multiple databases based on size and usage patterns
- **Security**: Domain administrators cannot access server-wide settings or other domains (even when sharing databases)
- **Performance**: Optimized database sizing - small domains can share resources, large domains get dedicated databases
- **Migration Flexibility**: Domains can be moved between databases by updating the registry
- **Cost Optimization**: Hosting providers can balance resource usage across shared and dedicated databases

### Domain-to-Database Resolution Implementation

The flexible database architecture requires a domain resolution system that maps domains to their respective databases:

#### Domain Resolution Service
```csharp
public interface IDomainResolver {
    Task<string> GetDatabaseNameAsync(string domain);
    Task InvalidateCacheAsync(string domain = null);
}

public class DomainResolver : IDomainResolver {
    private readonly IMemoryCache _cache;
    private readonly GlobalDbContext _globalContext;

    public async Task<string> GetDatabaseNameAsync(string domain) {
        return await _cache.GetOrCreateAsync($"domain:{domain}", async entry => {
            entry.SlidingExpiration = TimeSpan.FromHours(1);
            entry.Size = 1; // For cache size management

            return await _globalContext.DomainRegistry
                .Where(d => d.Name == domain && d.IsActive)
                .Select(d => d.DatabaseName)
                .FirstOrDefaultAsync();
        });
    }

    public async Task InvalidateCacheAsync(string domain = null) {
        if (domain != null) {
            _cache.Remove($"domain:{domain}");
        } else {
            // Clear all domain cache entries
            _cache.Remove("domain");
        }
    }
}
```

#### Database Context Factory
```csharp
public interface IDomainDbContextFactory {
    Task<DomainDbContext> CreateContextAsync(string domain);
}

public class DomainDbContextFactory : IDomainDbContextFactory {
    private readonly IDomainResolver _domainResolver;
    private readonly IConfiguration _configuration;

    public async Task<DomainDbContext> CreateContextAsync(string domain) {
        var databaseName = await _domainResolver.GetDatabaseNameAsync(domain);
        if (databaseName == null) {
            throw new InvalidOperationException($"Domain '{domain}' not found or inactive");
        }

        var basePath = _configuration["Database:DomainDatabasePath"];
        var connectionString = $"Data Source={Path.Combine(basePath, $"frimerki_{databaseName}.db")}";

        var options = new DbContextOptionsBuilder<DomainDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new DomainDbContext(options);
    }
}
```

#### Usage Examples
```csharp
// Get messages for a specific domain
public async Task<List<Message>> GetMessagesAsync(string domain, int userId) {
    using var context = await _domainDbContextFactory.CreateContextAsync(domain);
    return await context.Messages
        .Where(m => m.Users.Any(u => u.UserId == userId))
        .ToListAsync();
}

// Domain administration - move domain to different database
public async Task MoveDomainAsync(string domain, string newDatabaseName) {
    // 1. Export domain data from current database
    // 2. Import domain data to new database
    // 3. Update DomainRegistry.DatabaseName
    // 4. Invalidate cache

    await _globalContext.DomainRegistry
        .Where(d => d.Name == domain)
        .ExecuteUpdateAsync(d => d.SetProperty(x => x.DatabaseName, newDatabaseName));

    await _domainResolver.InvalidateCacheAsync(domain);
}
```

## Database Schema

### Core Tables

#### Users
```sql
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique user identifier
    Username TEXT NOT NULL,                                  -- Username part of email address (before @)
    DomainId INTEGER NOT NULL,                               -- Foreign key to DomainSettings table
    PasswordHash TEXT NOT NULL,                              -- BCrypt hashed password
    Salt TEXT NOT NULL,                                      -- Salt used for password hashing
    FullName TEXT,                                           -- User's display name
    Role TEXT NOT NULL DEFAULT 'User',                       -- User role: 'User', 'DomainAdmin', 'HostAdmin'
    CanReceive BOOLEAN DEFAULT 1,                            -- Whether user can receive emails
    CanLogin BOOLEAN DEFAULT 1,                              -- Whether user can log in to services
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- Account creation timestamp
    LastLogin DATETIME,                                      -- Last successful login timestamp
    FOREIGN KEY (DomainId) REFERENCES DomainSettings(Id),
    UNIQUE(Username, DomainId)                               -- Unique username per domain
);
```

**Role Enum Values:**
- `"User"` - Regular email user with access to their own emails and folders (stored in domain-specific database)
- `"DomainAdmin"` - Can manage users within their domain only (stored in domain-specific database)
- `"HostAdmin"` - Can manage all domains, users, and server settings (stored in global database with `@frimerki.local` email)

#### DomainSettings
```sql
CREATE TABLE DomainSettings (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique domain identifier
    Name TEXT NOT NULL UNIQUE,                               -- Domain name (e.g., example.com)
    CatchAllUserId INTEGER DEFAULT NULL,                     -- Optional catch-all user for unmatched addresses
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- Domain creation timestamp
    FOREIGN KEY (CatchAllUserId) REFERENCES Users(Id)
);
```

**Important Notes**:
- When domains share a database, this table contains **multiple records** (one per domain)
- When a domain has its own dedicated database, this table contains exactly **one record**
- The `IsActive` setting is managed in the global database's `DomainRegistry` table by the HostAdmin, while domain-specific settings like `CatchAllUserId` are managed within each domain's database
- The table name reflects its purpose as a domain-specific settings store

#### Messages
```sql
CREATE TABLE Messages (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique message identifier
    HeaderMessageId TEXT NOT NULL UNIQUE,                    -- RFC 2822 Message-ID header
    FromAddress TEXT NOT NULL,                               -- Sender's email address
    ToAddress TEXT,                                          -- Primary recipient address
    CcAddress TEXT,                                          -- CC recipients (comma-separated)
    BccAddress TEXT,                                         -- BCC recipients (comma-separated)
    Subject TEXT,                                            -- Message subject line
    Headers TEXT NOT NULL,                                   -- Complete raw email headers
    Body TEXT,                                               -- Plain text message body
    BodyHtml TEXT,                                           -- HTML message body
    MessageSize INTEGER NOT NULL,                            -- Message size in bytes (RFC2822 format)
    ReceivedAt DATETIME DEFAULT CURRENT_TIMESTAMP,           -- When message was received (INTERNALDATE)
    SentDate DATETIME,                                       -- Date from Date: header
    InReplyTo TEXT,                                          -- In-Reply-To header value
    References TEXT,                                         -- References header value
    BodyStructure TEXT,                                      -- MIME body structure (JSON)
    Envelope TEXT,                                           -- IMAP envelope structure (JSON)
    Uid INTEGER NOT NULL UNIQUE,                             -- IMAP UID (strictly ascending)
    UidValidity INTEGER DEFAULT 1,                           -- IMAP UIDVALIDITY value
    FOREIGN KEY (UidValidity) REFERENCES UidValiditySequence(Value)
);
```

#### MessageSearchIndex (FTS5)
```sql
CREATE VIRTUAL TABLE MessageSearchIndex USING fts5(
    MessageId UNINDEXED,                                     -- Reference to Messages.Id (not searchable)
    FromAddress,                                             -- Searchable sender address
    Subject,                                                 -- Searchable subject line
    Body,                                                    -- Searchable plain text body
    content='Messages',                                      -- Points to Messages table
    content_rowid='Id'                                       -- Uses Messages.Id as rowid
);
```

#### UidValiditySequence
```sql
CREATE TABLE UidValiditySequence (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Sequence identifier
    DomainId INTEGER NOT NULL,                               -- Domain this sequence belongs to
    Value INTEGER NOT NULL DEFAULT 1,                        -- Current UIDVALIDITY value
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- When this UIDVALIDITY was created
    FOREIGN KEY (DomainId) REFERENCES DomainSettings(Id),
    UNIQUE(DomainId, Value)                                  -- One UIDVALIDITY per domain
);
```

#### MessageFlags
```sql
CREATE TABLE MessageFlags (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique flag assignment identifier
    MessageId INTEGER NOT NULL,                              -- Message this flag applies to
    UserId INTEGER NOT NULL,                                 -- User who owns this flag state
    FlagName TEXT NOT NULL,                                  -- Flag name (\Seen, \Answered, etc.)
    IsSet BOOLEAN DEFAULT 1,                                 -- Whether flag is set or cleared
    ModifiedAt DATETIME DEFAULT CURRENT_TIMESTAMP,           -- When flag was last modified
    FOREIGN KEY (MessageId) REFERENCES Messages(Id),
    FOREIGN KEY (UserId) REFERENCES Users(Id),
    UNIQUE(MessageId, UserId, FlagName)                      -- One flag state per message per user
);
```

#### Folders
```sql
CREATE TABLE Folders (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique folder identifier
    UserId INTEGER NOT NULL,                                 -- Owner of this folder
    Name TEXT NOT NULL,                                      -- Full folder path (e.g., INBOX/Work)
    Delimiter TEXT DEFAULT '/',                              -- Hierarchy delimiter character
    SystemFolderType TEXT DEFAULT NULL,                      -- System folder type or NULL for user folders
    Attributes TEXT,                                         -- Folder attributes (\Marked, \Unmarked, etc.)
    UidNext INTEGER DEFAULT 1,                               -- Next UID to be assigned in this folder
    UidValidity INTEGER DEFAULT 1,                           -- UIDVALIDITY for this folder
    Exists INTEGER DEFAULT 0,                                -- Number of messages in folder
    Recent INTEGER DEFAULT 0,                                -- Number of messages with \Recent flag
    Unseen INTEGER DEFAULT 0,                                -- Number of messages without \Seen flag
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
    Uid INTEGER NOT NULL,                                    -- IMAP UID for this message in this folder
    SequenceNumber INTEGER,                                  -- Current sequence number (dynamic)
    ReceivedAt DATETIME DEFAULT CURRENT_TIMESTAMP,           -- When this user received the message
    FOREIGN KEY (UserId) REFERENCES Users(Id),
    FOREIGN KEY (MessageId) REFERENCES Messages(Id),
    FOREIGN KEY (FolderId) REFERENCES Folders(Id),
    UNIQUE(FolderId, Uid),                                   -- UIDs must be unique within folder
    UNIQUE(UserId, MessageId, FolderId)                      -- Prevent duplicate message in same folder
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
    FileExtension TEXT,                                      -- Original file extension (e.g., '.pdf', '.jpg')
    FilePath TEXT,                                           -- Full path to stored file (includes /attachments/guid.ext)
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- When attachment was stored
    FOREIGN KEY (MessageId) REFERENCES Messages(Id)
);
```

#### DKIMKeys
```sql
CREATE TABLE DKIMKeys (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,                    -- Unique DKIM key identifier
    DomainId INTEGER NOT NULL,                               -- Domain this key belongs to
    Selector TEXT NOT NULL,                                  -- DKIM selector (e.g., 'default', 'mail')
    PrivateKey TEXT NOT NULL,                                -- RSA private key for signing (PEM format)
    PublicKey TEXT NOT NULL,                                 -- RSA public key for DNS TXT record
    IsActive BOOLEAN DEFAULT 1,                              -- Whether this key is active for signing
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,            -- Key creation timestamp
    FOREIGN KEY (DomainId) REFERENCES DomainSettings(Id),
    UNIQUE(DomainId, Selector)                               -- One selector per domain
);
```

## Protocol Support

### SMTP (Simple Mail Transfer Protocol)
- **Port 25**: Standard SMTP (unencrypted)
- **Port 587**: SMTP with STARTTLS
- **Port 465**: SMTP over SSL/TLS
- **Features**:
  - Message routing and delivery
  - Authentication (AUTH LOGIN, AUTH PLAIN, OAUTH2)
  - STARTTLS support
  - Message size limits
  - Rate limiting
  - DKIM signing for outbound mail
  - SPF checking for inbound mail
  - Basic spam filtering

### IMAP (Internet Message Access Protocol)
- **Port 143**: Standard IMAP (unencrypted)
- **Port 993**: IMAP over SSL/TLS
- **Features**:
  - **Folder hierarchy support**: Full support for nested folders with configurable delimiters
  - **Message flags and status**: Complete IMAP flag system (\Seen, \Answered, \Flagged, \Deleted, \Draft, \Recent)
  - **Server-side search**: Support for all IMAP SEARCH criteria including text, headers, dates, flags
  - **Partial message fetching**: BODY[section] and BODY.PEEK[section] with partial ranges
  - **UID support**: Proper UID management with UIDVALIDITY and UIDNEXT
  - **IDLE command**: Real-time push notifications for mailbox changes
  - **Multi-folder synchronization**: Concurrent folder access and status updates
  - **Message sequence numbers**: Dynamic sequence number management
  - **Envelope and body structure**: Parsed MIME structure and RFC2822 envelope
  - **Quota support**: Mailbox size and message count limits (future)
  - **Access Control Lists**: Shared folder permissions (future)
  - **IMAP Extensions**: SORT, THREAD, CONDSTORE (future enhancements)

### POP3 (Post Office Protocol)
- **Port 110**: Standard POP3 (unencrypted)
- **Port 995**: POP3 over SSL/TLS
- **Features**:
  - Download and delete model
  - Leave messages on server option
  - APOP authentication
  - Basic message retrieval

### Future Protocol Support (Phase 6)
- **JMAP**: Modern JSON-based email protocol for web/mobile clients
- **LMTP**: Local Mail Transfer Protocol for efficient local delivery
- **ManageSieve**: Server-side mail filtering management

## Attachment Storage Strategy

### File Storage Design
- **Storage Location**: `/attachments` directory (configurable)
- **File Naming**: GUID-based filenames with original extension to enable content-type detection
- **Static Serving**: Files served directly by web server for performance
- **URL Format**: `/attachments/{guid}.{extension}` (relative path with extension)

### Security Considerations
- **Initial Implementation**: Static file serving for simplicity
- **Future Enhancement**: Authentication-protected streaming via API endpoint
- **File Access**: Attachment path included in message response for client-side URL construction
- **GUID Benefits**:
  - Prevents filename conflicts
  - Obscures original filenames while preserving extensions
  - Makes file enumeration difficult
  - Enables easy cleanup of orphaned files
- **Extension Preservation**: Original file extensions preserved for proper MIME type detection
- **Content-Type Headers**: Web server automatically sets correct Content-Type based on file extension

### Example Attachment Response
```json
{
  "id": 123,
  "subject": "Document with attachments",
  "attachments": [
    {
      "fileName": "document.pdf",
      "contentType": "application/pdf",
      "size": 1024000,
      "path": "/attachments/550e8400-e29b-41d4-a716-446655440000.pdf"
    },
    {
      "fileName": "photo.jpg",
      "contentType": "image/jpeg",
      "size": 2048000,
      "path": "/attachments/6ba7b810-9dad-11d1-80b4-00c04fd430c8.jpg"
    }
  ]
}
```

## Backup Storage Strategy

### File Storage Design
- **Storage Location**: `/backups` directory (configurable)
- **File Naming**: Date-time with GUID format: `YYYY-MM-DD-HH-mm-ss-{guid}.zip`
- **Static Serving**: Files served directly by web server for performance
- **URL Format**: `/backups/{datetime-guid}.zip` (relative path)

### Backup ID Management
- **Backup ID Format**: Date-time portion from filename (e.g., `2025-07-28-14-30-15`)
- **File Identification**: Each backup file contains both timestamp and GUID for uniqueness
- **DELETE Endpoint**: Uses date-time portion as `{backupId}` parameter
- **Collision Handling**: GUID suffix prevents conflicts for backups created in same second

### Security Considerations
- **Initial Implementation**: Static file serving for simplicity (HostAdmin only access via web server config)
- **Future Enhancement**: Authentication-protected streaming via API endpoint
- **File Access**: Download URLs included in backup list response for direct client access
- **GUID Benefits**:
  - Prevents filename enumeration attacks
  - Ensures uniqueness even with rapid backup creation
  - Makes unauthorized backup discovery difficult
  - Enables easy cleanup of orphaned files

### Example Backup List Response
```json
{
  "backups": [
    {
      "id": "2025-07-28-14-30-15",
      "createdAt": "2025-07-28T14:30:15Z",
      "size": 52428800,
      "downloadUrl": "/backups/2025-07-28-14-30-15-550e8400-e29b-41d4-a716-446655440000.zip",
      "description": "Scheduled daily backup"
    },
    {
      "id": "2025-07-28-09-15-42",
      "createdAt": "2025-07-28T09:15:42Z",
      "size": 48234496,
      "downloadUrl": "/backups/2025-07-28-09-15-42-6ba7b810-9dad-11d1-80b4-00c04fd430c8.zip",
      "description": "Manual backup before upgrade"
    }
  ],
  "totalSize": 100663296,
  "totalCount": 2
}
```

## Log Storage Strategy

### Hybrid Approach Design
- **API Endpoint**: `/api/server/logs` - Filtered/paginated log entries for querying and searching
- **Static File Serving**: `/logs/{datetime}.log` - Direct log file downloads for full access
- **File List Endpoint**: `/api/server/logfiles` - List available log files with download URLs

### File Storage Design
- **Storage Location**: `/logs` directory (configurable)
- **File Naming**: Date-based format: `frimerki-YYYY-MM-DD.log`
- **Static Serving**: Files served directly by web server for performance
- **URL Format**: `/logs/frimerki-{date}.log` (relative path)

### Log File Management
- **Daily Rotation**: New log file created each day
- **File Identification**: Date-based naming for easy chronological access
- **Retention Policy**: Configurable log retention period (default: 30 days)
- **Compression**: Optional gzip compression for older log files

### Security Considerations
- **Initial Implementation**: Static file serving for simplicity (HostAdmin only access via web server config)
- **Future Enhancement**: Authentication-protected streaming via API endpoint
- **File Access**: Download URLs included in log file list response for direct client access
- **Access Control**: Log files restricted to HostAdmin role only

### Example Log File List Response
```json
{
  "logFiles": [
    {
      "fileName": "frimerki-2025-07-28.log",
      "date": "2025-07-28",
      "size": 1048576,
      "downloadUrl": "/logs/frimerki-2025-07-28.log",
      "isToday": true,
      "compressed": false
    },
    {
      "fileName": "frimerki-2025-07-27.log.gz",
      "date": "2025-07-27",
      "size": 262144,
      "downloadUrl": "/logs/frimerki-2025-07-27.log.gz",
      "isToday": false,
      "compressed": true
    }
  ],
  "totalSize": 1310720,
  "totalCount": 2,
  "retentionDays": 30
}
```

## Web API Endpoints

### Authentication Endpoints
```
POST   /api/session             - Create session (login)
DELETE /api/session             - Delete session (logout)
GET    /api/session             - Get current session/user info (auto-refreshes token)
POST   /api/session/refresh     - Refresh access token using refresh token
POST   /api/session/revoke      - Revoke refresh token
GET    /api/session/status      - Check authentication status (lightweight)
```

### User Management
```
GET    /api/users                               - List all users (HostAdmin) or domain users (DomainAdmin)
POST   /api/users                               - Create new user (HostAdmin/DomainAdmin)
GET    /api/users/{email}                       - Get user details: full data for own account/admin, minimal for others, 404 if not found
PUT    /api/users/{email}                       - Update user (own account or admin)
PATCH  /api/users/{email}                       - Partial update user (own account or admin)
PATCH  /api/users/{email}/password              - Update user password specifically
DELETE /api/users/{email}                       - Delete user (HostAdmin/DomainAdmin)
GET    /api/users/{email}/stats                 - Get user statistics (storage, message count, etc.)
```

### Message Management
```
GET    /api/messages                     - List messages with filtering (?q=search+terms&folder=INBOX&flags=unread&skip=0&take=50)
GET    /api/messages/{id}                - Get complete message (includes envelope, bodystructure, flags, attachments)
POST   /api/messages                     - Send new message
PUT    /api/messages/{id}                - Update message (flags, folder move, etc.)
PATCH  /api/messages/{id}                - Partially update message (flags, folder move, content for drafts)
DELETE /api/messages/{id}                - Delete message (move to trash)
```

### Folder Management
```
GET    /api/folders                           - List user folders with hierarchy (includes subscribed flag)
GET    /api/folders/{name}                    - Get folder details and status (EXISTS, RECENT, UNSEEN, UIDNEXT, UIDVALIDITY, subscribed)
POST   /api/folders                           - Create new folder
PUT    /api/folders/{name}                    - Update folder (rename, subscription status, etc.)
PATCH  /api/folders/{name}                    - Partially update folder
DELETE /api/folders/{name}                    - Delete folder
```

**Note on Folder Names in URLs:**
- Folder names containing forward slashes (/) must be URL-encoded as `%2F`
- Examples:
  - `INBOX` → `/api/folders/INBOX`
  - `INBOX/Work` → `/api/folders/INBOX%2FWork`
  - `INBOX/Projects/2024` → `/api/folders/INBOX%2FProjects%2F2024`

### Domain Management (Admin)
```
GET    /api/domains                         - List domains (HostAdmin sees all, DomainAdmin sees own)
POST   /api/domains                         - Add new domain (HostAdmin only), returns 409 if domain exists
GET    /api/domains/{domainname}            - Get domain details including statistics, DKIM info, and usage data (raw numbers for frontend formatting)
PUT    /api/domains/{domainname}            - Update domain (HostAdmin only)
PATCH  /api/domains/{domainname}            - Partially update domain (HostAdmin only)
DELETE /api/domains/{domainname}            - Delete domain (HostAdmin only)
GET    /api/domains/{domainname}/dkim       - Get DKIM public key for DNS setup
POST   /api/domains/{domainname}/dkim       - Generate new DKIM key pair
POST   /api/domains/{domainname}/move       - Move domain to different database (HostAdmin only)
GET    /api/databases                       - List all domain databases with statistics (HostAdmin only)
GET    /api/databases/{databasename}        - Get database details and domain list (HostAdmin only)
```

**New Database Management Features:**
- **Domain Migration**: `POST /api/domains/{domainname}/move` allows moving domains between databases
- **Database Statistics**: View which domains share databases and their resource usage
- **Flexible Assignment**: New domains can be assigned to existing shared databases or get dedicated ones

#### POST /api/domains - Create New Domain

**Request Body Options:**

**Option 1: Dedicated Database (Default)**
```json
{
  "name": "newcorp.com"
  // databaseName will be auto-generated as "domain_newcorp_com"
  // Database will be created automatically
}
```

**Option 2: Use Existing Shared Database**
```json
{
  "name": "smallbiz.net",
  "databaseName": "shared_small_domains"
  // Will fail if database doesn't exist
}
```

**Option 3: Create New Shared Database**
```json
{
  "name": "startup.io",
  "databaseName": "shared_tech_startups",
  "createDatabase": true
  // Will create the database, fails if it already exists
}
```

**Response Examples:**

**Success Response (201 Created):**
```json
{
  "name": "newcorp.com",
  "databaseName": "domain_newcorp_com",
  "isActive": true,
  "isDedicated": true,
  "createdAt": "2025-07-30T14:30:00Z",
  "databaseCreated": true,
  "initialSetup": {
    "adminUserCreated": false,
    "defaultFoldersCreated": true,
    "dkimKeysGenerated": false
  }
}
```

**Error Response (409 Conflict - Domain Exists):**
```json
{
  "error": "DomainAlreadyExists",
  "message": "Domain 'newcorp.com' already exists",
  "details": {
    "existingDomain": "newcorp.com",
    "currentDatabase": "domain_newcorp_com"
  }
}
```

**Error Response (400 Bad Request - Database Not Found):**
```json
{
  "error": "DatabaseNotFound",
  "message": "Database 'shared_small_domains' does not exist",
  "suggestion": "Set 'createDatabase' to true to create the database"
}
```

**Error Response (409 Conflict - Database Already Exists):**
```json
{
  "error": "DatabaseAlreadyExists",
  "message": "Database 'shared_tech_startups' already exists",
  "suggestion": "Remove 'createDatabase' flag to use existing database or choose a different name"
}
```

### Server Management (HostAdmin)
```
GET    /api/server/status              - Server status and statistics (HostAdmin only)
GET    /api/server/health              - Health check endpoint (HostAdmin only)
GET    /api/server/metrics             - Performance metrics (HostAdmin only)
GET    /api/server/logs                - Server logs with filtering/pagination (HostAdmin only)
GET    /api/server/logfiles            - List available log files with download URLs (HostAdmin only)
GET    /api/server/settings            - Get server settings (HostAdmin only)
PUT    /api/server/settings            - Update server settings (HostAdmin only)
POST   /api/server/backup              - Create backup (HostAdmin only)
POST   /api/server/restore             - Restore from backup (HostAdmin only)
GET    /api/server/backups             - List available backups with download URLs (HostAdmin only)
DELETE /api/server/backup/{backupId}   - Delete specific backup by date-time ID (HostAdmin only)
POST   /api/server/restart             - Restart server services (HostAdmin only)
GET    /api/server/info                - Get system information (HostAdmin only)
```

### Health & System Endpoints
```
GET    /api/health                     - Basic health check (public)
GET    /api/health/info                - Server information (public)
```

### Mail Processing (Future Enhancement)
```
GET    /api/mail-rules          - List user mail filtering rules
POST   /api/mail-rules          - Create mail filtering rule
PUT    /api/mail-rules/{id}     - Update mail filtering rule
DELETE /api/mail-rules/{id}     - Delete mail filtering rule
GET    /api/mail-queue          - View mail queue status (HostAdmin only)
POST   /api/mail-queue/retry    - Retry failed messages (HostAdmin only)
```

### Real-time Endpoints (SignalR)
```
/hubs/email                              - Real-time email notifications
  - NewMessage(folderId, messageInfo)    - New message arrived
  - MessageFlags(messageId, flags)       - Message flags changed
  - MessageExpunged(folderId, uid)       - Message permanently deleted
  - FolderUpdated(folderId, exists, recent, unseen) - Folder status changed
  - FolderCreated(folderInfo)            - New folder created
  - FolderDeleted(folderId)              - Folder deleted
  - FolderRenamed(folderId, newName)     - Folder renamed
```

## User Endpoint Behavior

### GET /api/users/{email}

This endpoint implements intelligent response behavior based on user permissions:

**For Non-existent Users:**
- Returns `404 Not Found` status
- Clients can use this to check email availability (404 = available)

**For Existing Users:**
- **Admin users** (HostAdmin/DomainAdmin): Returns complete user details
- **User accessing own account**: Returns complete user details
- **Other authenticated users**: Returns minimal user information (email and username only)

**Response Examples:**

Admin or own account response:
```json
{
  "username": "john.doe",
  "email": "john.doe@example.com",
  "fullName": "John Doe",
  "role": "User",
  "canReceive": true,
  "canLogin": true,
  "createdAt": "2025-01-15T10:30:00Z",
  "lastLogin": "2025-01-27T08:45:00Z",
  "domainName": "example.com",
  "stats": { ... }
}
```

Non-admin accessing other user:
```json
{
  "email": "john.doe@example.com",
  "username": "john.doe"
}
```

## API Response Formats

### Pagination

All list endpoints use skip/take pagination with next URLs for efficient navigation:

**Query Parameters:**
- `skip` - Number of items to skip (default: 0)
- `take` - Number of items to take (default: 50, max: 100)

**Response Structure:**
```json
{
  "items": [...],
  "pagination": {
    "skip": 0,
    "take": 50,
    "totalCount": 156,
    "nextUrl": "/api/messages?skip=50&take=50&folder=INBOX&flags=unread"
  }
}
```

**Notes:**
- The presence of `nextUrl` indicates more items are available
- The absence of `nextUrl` indicates this is the last page
- The `nextUrl` preserves all applied filters and sorting parameters
- Clients can simply follow the `nextUrl` without constructing URLs

### Message List Response Example

```json
{
  "messages": [
    {
      "id": 123,
      "subject": "Meeting tomorrow",
      "from": "john@example.com",
      "to": "me@example.com",
      "sentDate": "2025-01-27T10:30:00Z",
      "flags": { "seen": false, "flagged": true },
      "folder": "INBOX",
      "hasAttachments": true,
      "size": 4286
    }
  ],
  "pagination": {
    "skip": 0,
    "take": 50,
    "totalCount": 156,
    "nextUrl": "/api/messages?skip=50&take=50&folder=INBOX&flags=unread"
  },
  "appliedFilters": {
    "folder": "INBOX",
    "flags": "unread"
  }
}
```

### Domain Management Response Examples

#### GET /api/domains Response
```json
{
  "domains": [
    {
      "name": "bigcorp.com",
      "databaseName": "domain_bigcorp_com",
      "isActive": true,
      "userCount": 500,
      "messageCount": 25000,
      "storageUsed": 5368709120,
      "isDedicated": true,
      "createdAt": "2025-01-15T10:30:00Z"
    },
    {
      "name": "smallbiz.com",
      "databaseName": "shared_small_domains",
      "isActive": true,
      "userCount": 5,
      "messageCount": 150,
      "storageUsed": 15728640,
      "isDedicated": false,
      "createdAt": "2025-02-20T14:15:00Z"
    },
    {
      "name": "startup.com",
      "databaseName": "shared_small_domains",
      "isActive": true,
      "userCount": 8,
      "messageCount": 320,
      "storageUsed": 31457280,
      "isDedicated": false,
      "createdAt": "2025-03-10T09:45:00Z"
    }
  ]
}
```

#### GET /api/databases Response
```json
{
  "databases": [
    {
      "name": "domain_bigcorp_com",
      "filePath": "./data/domains/frimerki_domain_bigcorp_com.db",
      "fileSize": 5368709120,
      "domains": ["bigcorp.com"],
      "totalUsers": 500,
      "totalMessages": 25000,
      "isDedicated": true,
      "createdAt": "2025-01-15T10:30:00Z"
    },
    {
      "name": "shared_small_domains",
      "filePath": "./data/domains/frimerki_shared_small_domains.db",
      "fileSize": 52428800,
      "domains": ["smallbiz.com", "startup.com", "family.org"],
      "totalUsers": 23,
      "totalMessages": 850,
      "isDedicated": false,
      "createdAt": "2025-02-20T14:15:00Z"
    }
  ],
  "totalSize": 5421137920,
  "totalDomains": 4,
  "totalUsers": 523,
  "totalMessages": 25850
}
```

#### POST /api/domains/{domainname}/move Request/Response
```json
// Request
{
  "targetDatabaseName": "shared_medium_domains",
  "createIfNotExists": true
}

// Response
{
  "success": true,
  "domain": "example.com",
  "previousDatabase": "shared_small_domains",
  "newDatabase": "shared_medium_domains",
  "migrationDetails": {
    "usersMigrated": 12,
    "messagesMigrated": 450,
    "attachmentsMigrated": 23,
    "migrationTimeMs": 2500
  }
}
```

## Security Features

### Encryption
- **SSL/TLS Support**: All protocols support encrypted connections
- **Certificate Management**: Automatic certificate generation and renewal
- **Perfect Forward Secrecy**: Modern cipher suites

### Authentication
- **Password Hashing**: BCrypt with salt for all user types
- **JWT Tokens**: For web API authentication
- **Session Management**: Secure session handling
- **Multi-Database Authentication**:
  - HostAdmin accounts authenticate against global database (`admin@frimerki.local`)
  - Domain users authenticate against their domain-specific database (`user@domain.com`)
- **Multi-factor Authentication**: TOTP support (future enhancement)

### Authorization
- **Role-based Access**: User, DomainAdmin, HostAdmin roles
- **User Permissions**: Access to own emails, folders, and account settings
- **Domain Admin Permissions**: Manage users within their own domain
- **Host Admin Permissions**: Full access to all domains, users, and server settings
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
    "BackupsPath": "/backups",
    "LogsPath": "/logs",
    "EnableSMTP": true,
    "EnableIMAP": true,
    "EnablePOP3": true,
    "EnableWebAPI": true,
    "ReservedDomain": "frimerki.local"
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
    "GlobalConnectionString": "Data Source=frimerki_global.db",
    "DomainDatabasePath": "./data/domains/",
    "BackupInterval": "24h",
    "RegistryCacheExpiration": "1h",
    "RegistryCacheSize": 1000
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
- **SQLite**: Embedded database with FTS5 full-text search
- **Multi-Database Architecture**: Global server database + domain-specific databases
- **SQLite.Interop**: Native SQLite library

#### Sequence Number Management
```sql
-- Trigger to maintain sequence numbers when messages are expunged
CREATE TRIGGER update_sequence_numbers
AFTER DELETE ON UserMessages
FOR EACH ROW
BEGIN
    UPDATE UserMessages
    SET SequenceNumber = SequenceNumber - 1
    WHERE FolderId = OLD.FolderId
    AND SequenceNumber > OLD.SequenceNumber;
END;

-- Trigger to assign sequence numbers to new messages
CREATE TRIGGER assign_sequence_number
AFTER INSERT ON UserMessages
FOR EACH ROW
BEGIN
    UPDATE UserMessages
    SET SequenceNumber = (
        SELECT COALESCE(MAX(SequenceNumber), 0) + 1
        FROM UserMessages
        WHERE FolderId = NEW.FolderId
    )
    WHERE rowid = NEW.rowid;
END;
```

## IMAP Protocol Compliance

### Core IMAP4rev1 Support

The Frímerki server implementation ensures full compliance with RFC 3501 (IMAP4rev1):

#### Connection States
- **Not Authenticated**: Initial connection state requiring authentication
- **Authenticated**: User authenticated but no mailbox selected
- **Selected**: Mailbox selected for message operations
- **Logout**: Connection termination state

#### Required Commands
**Any State:**
- `CAPABILITY` - List server capabilities
- `NOOP` - No operation (keepalive)
- `LOGOUT` - Close connection

**Not Authenticated State:**
- `STARTTLS` - Upgrade to TLS encryption
- `AUTHENTICATE` - SASL authentication (PLAIN, GSSAPI, etc.)
- `LOGIN` - Plain text authentication (requires TLS)

**Authenticated State:**
- `SELECT` - Select mailbox for read-write access
- `EXAMINE` - Select mailbox for read-only access
- `CREATE` - Create new mailbox
- `DELETE` - Delete mailbox
- `RENAME` - Rename mailbox
- `SUBSCRIBE/UNSUBSCRIBE` - Manage folder subscriptions
- `LIST` - List mailboxes with attributes
- `LSUB` - List subscribed mailboxes
- `STATUS` - Get mailbox status without selecting
- `APPEND` - Add message to mailbox

**Selected State:**
- `CHECK` - Checkpoint mailbox
- `CLOSE` - Close current mailbox
- `EXPUNGE` - Permanently remove deleted messages
- `SEARCH` - Search messages by criteria
- `FETCH` - Retrieve message data
- `STORE` - Update message flags
- `COPY` - Copy messages to another mailbox
- `UID` - Execute commands using UIDs instead of sequence numbers

#### Message Attributes Support
- **Sequence Numbers**: Dynamic 1-based numbering
- **Unique Identifiers (UIDs)**: Persistent, strictly ascending 32-bit values
- **UIDVALIDITY**: Detects UID reuse scenarios
- **Flags**: System flags (\Seen, \Answered, \Flagged, \Deleted, \Draft, \Recent) and custom keywords
- **Internal Date**: Server-assigned receive timestamp
- **RFC2822 Size**: Message size in standard format
- **Envelope**: Parsed header structure
- **Body Structure**: MIME structure parsing

#### Search Capabilities
Support for all IMAP SEARCH criteria:
- Text searches: BODY, TEXT, HEADER, SUBJECT, FROM, TO, CC, BCC
- Date searches: BEFORE, ON, SINCE, SENTBEFORE, SENTON, SENTSINCE
- Size searches: LARGER, SMALLER
- Flag searches: ANSWERED, DELETED, DRAFT, FLAGGED, SEEN, RECENT, etc.
- Logical operators: AND, OR, NOT
- Sequence sets and UID ranges

#### Folder Features
- **Hierarchy**: Nested folder structure with configurable delimiters
- **Attributes**: \Noinferiors, \Noselect, \Marked, \Unmarked
- **Special Folders**: INBOX (case-insensitive), system folders
- **Subscriptions**: Server-side subscription list management

#### Extensions (Future)
- **IDLE**: Real-time notifications for mailbox changes
- **SORT**: Server-side message sorting
- **THREAD**: Message threading by conversation
- **QUOTA**: Storage quota management
- **ACL**: Access control lists for shared folders
- **CONDSTORE**: Conditional STORE and quick flag sync

### IMAP Response Format Compliance

#### Status Responses
```
* OK [CAPABILITY IMAP4rev1 STARTTLS AUTH=PLAIN LOGINDISABLED] Frímerki ready
* OK [ALERT] System message
* OK [BADCHARSET (UTF-8)] Charset not supported
* OK [PERMANENTFLAGS (\Answered \Flagged \Deleted \Seen \Draft \*)] Flags permitted
* OK [READ-ONLY] Mailbox selected read-only
* OK [READ-WRITE] Mailbox selected read-write
* OK [TRYCREATE] Create mailbox first
* OK [UIDNEXT 4392] Predicted next UID
* OK [UIDVALIDITY 3857529045] UIDs valid
* OK [UNSEEN 17] First unseen message
```

#### Data Responses
```
* 172 EXISTS                             -- Message count
* 1 RECENT                               -- Recent message count
* FLAGS (\Answered \Flagged \Deleted \Seen \Draft)  -- Available flags
* SEARCH 2 84 882                        -- Search results
* LIST () "/" INBOX                      -- Folder listing
* STATUS INBOX (MESSAGES 231 UIDNEXT 44292) -- Folder status
```

#### Fetch Response Examples
```
* 12 FETCH (FLAGS (\Seen) UID 4827313 INTERNALDATE "17-Jul-1996 02:44:25 -0700"
  RFC822.SIZE 4286 ENVELOPE ("Wed, 17 Jul 1996 02:23:25 -0700 (PDT)"
  "IMAP4rev1 WG mtg summary" (("Terry Gray" NIL "gray" "example.com")) ...))
```

### Implementation Priority

#### Phase 1: Core IMAP (Essential)
- Basic authentication and connection management
- SELECT/EXAMINE with proper folder selection
- FETCH with FLAGS, UID, INTERNALDATE, RFC822.SIZE
- STORE for flag updates
- SEARCH with basic text and flag criteria
- Proper sequence number and UID management

#### Phase 2: Standard Compliance (Important)
- Full SEARCH criteria support
- Complete FETCH response types (ENVELOPE, BODYSTRUCTURE)
- Folder operations (CREATE, DELETE, RENAME, LIST, LSUB)
- APPEND command for message upload
- COPY command for message copying
- Proper EXPUNGE handling

#### Phase 3: Advanced Features (Enhancement)
- IDLE for real-time notifications
- Partial FETCH with BODY[section] syntax
- Advanced folder hierarchy management
- Subscription management
- STATUS command optimization

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
TXT   default._domainkey.example.com → "v=DKIM1; k=rsa; p=MIGfMA0GCSqGSIb3DQEBAQUAA..."
```

### Initial Setup

#### HostAdmin Account Creation
On first startup, Frímerki should:
1. Create the global database (`frimerki_global.db`) if it doesn't exist
2. Check if any HostAdmin accounts exist in the global Users table
3. If no HostAdmin accounts exist, prompt for initial admin account creation:
   - Username (e.g., `admin`)
   - Full email will be `{username}@frimerki.local` (e.g., `admin@frimerki.local`)
   - Password (with confirmation)
   - Full Name (optional)
4. Create the first HostAdmin account with secure password hashing

#### Subsequent HostAdmin Management
- Additional HostAdmin accounts can be created through the API by existing HostAdmins
- HostAdmin accounts use the reserved domain `frimerki.local` to distinguish from regular domain users
- Authentication checks the global database for `@frimerki.local` emails, domain databases for others

#### Domain Database Creation Strategy
When creating new domains, the system follows this simple logic:

**Default Behavior - Dedicated Database:**
```json
{
  "domain": "newdomain.com"
  // Creates: frimerki_domain_newdomain_com.db (auto-generated name)
  // Always creates the database
}
```

**Shared Database - Existing:**
```json
{
  "domain": "smalldomain.com",
  "databaseName": "shared_small_domains"
  // Uses existing database, fails if not found
}
```

**Shared Database - Create New:**
```json
{
  "domain": "startup.com",
  "databaseName": "shared_tech_startups",
  "createDatabase": true
  // Creates database, fails if it already exists
}
```

**Database Creation Process:**
1. If no `databaseName` specified: auto-generate dedicated database name
2. If `databaseName` specified: check if database exists
3. If database doesn't exist and `createDatabase` is true: create it
4. If database doesn't exist and `createDatabase` is false/missing: return error
5. If database exists and `createDatabase` is true: return error (conflict)
6. Add domain record to DomainRegistry with database mapping
7. Create DomainSettings record in target database
8. Initialize default folders and system settings
9. Update domain resolution cache## Development Phases

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
2. DKIM signing and verification
3. Rate limiting and spam filtering
4. Admin management interface
5. Backup and restore functionality

### Phase 5: Frontend Applications
1. User email interface (SPA)
2. Admin management interface
3. Mobile-responsive design
4. Progressive Web App features

### Phase 6: Advanced Email Features
1. JMAP protocol implementation
2. Mail processing pipeline (Mailet-style)
3. Server-side mail filtering rules
4. Advanced spam and virus filtering
5. Mail aliases and forwarding
6. Auto-responders and vacation messages

### Phase 7: Enterprise Features
1. Advanced monitoring and metrics
2. Mail archiving capabilities
3. Distribution lists
4. Multi-tenancy enhancements
5. Command-line administration tools

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

This specification provides a comprehensive foundation for building Frímerki, a modern, lightweight email server in C# with minimal dependencies and hardware requirements.
