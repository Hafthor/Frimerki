# Frímerki Email Server - TODO

## Critical Missing Functionality

### Server Management (High Priority)
Located in `src/Frimerki.Server/Controllers/ServerController.cs`:

1. **Backup & Restore Operations**
   - `CreateBackupAsync()` - Currently returns placeholder 1MB size
   - `RestoreBackupAsync()` - Not implemented, returns success without action
   - **Impact**: Critical for data protection and disaster recovery

2. **System Resource Monitoring**
   - `GetServerMetricsAsync()` - Returns basic placeholder metrics
   - **Needed**: Real CPU, memory, disk usage monitoring
   - **Priority**: High for production monitoring

3. **Configuration Management**
   - `UpdateServerSettingsAsync()` - Basic implementation, may need enhancement
   - **Needed**: Validation of server configuration changes

### Domain Management (Medium Priority)
Located in `src/Frimerki.Server/Controllers/DomainsController.cs`:

4. **Domain Statistics**
   - `GetDomainStatsAsync()` - Returns placeholder statistics
   - **Needed**: Real storage usage, message counts per domain

5. **Import/Export Operations**
   - `ExportDomainAsync()` - Not implemented
   - `ImportDomainAsync()` - Not implemented
   - **Impact**: Required for domain migration and backup

6. **Domain Validation**
   - `ValidateDomainAsync()` - Returns placeholder validation
   - **Needed**: DNS validation, MX record checking

### IMAP Protocol (Phase 2 Features)
Located in `src/Frimerki.Protocols/Imap/ImapSession.cs`:

7. **Advanced IMAP Commands**
   - `FETCH` command - Currently returns placeholder response for complex fetches
   - `SEARCH` command - Returns "SEARCH completed" without actual search
   - **Impact**: Critical for full IMAP compliance

8. **IMAP Extensions** (Future Enhancement)
   - IDLE support for real-time updates
   - CONDSTORE for conditional store
   - QRESYNC for quick resynchronization
   - **Priority**: Low, but important for advanced email clients

### Email Processing
Located in `src/Frimerki.Services/Message/MessageService.cs`:

9. **Message Threading**
   - Basic message operations exist, but no conversation threading
   - **Needed**: Thread ID calculation and conversation grouping

### User Service Enhancement
Located in `src/Frimerki.Services/User/UserService.cs`:

10. **Enhanced User Statistics**
    - `GetUserStatsAsync()` - Returns basic stats
    - **Needed**: Storage quotas, usage patterns, login history

## Phase 2 Features (Future Enhancements)

### Authentication & Security
11. **Multi-Factor Authentication (MFA)**
    - No current implementation
    - **Priority**: Medium for enterprise deployments

12. **OAuth2/OpenID Integration**
    - Basic JWT authentication exists
    - **Enhancement**: External identity provider support

### Protocol Enhancements
13. **SMTP Extensions**
    - Basic SMTP implemented
    - **Needed**: SMTP AUTH mechanisms beyond PLAIN
    - **Future**: DKIM signing, SPF validation

14. **POP3 Extensions**
    - Basic POP3 functionality complete
    - **Future**: APOP authentication

### Storage & Performance
15. **Message Storage Optimization**
    - Current SQLite implementation functional
    - **Future**: Large attachment handling, deduplication

16. **Caching Layer**
    - No explicit caching currently
    - **Enhancement**: Redis integration for frequently accessed data

### Monitoring & Operations
17. **Health Checks**
    - Basic health endpoint exists
    - **Enhancement**: Detailed service health monitoring

18. **Metrics Collection**
    - Basic metrics in ServerController
    - **Enhancement**: Prometheus/OpenTelemetry integration

### Web Interface
19. **Admin Dashboard Enhancements**
    - Basic API endpoints exist
    - **Future**: Real-time monitoring, log viewing

20. **User Portal**
    - No current web interface for end users
    - **Future**: Webmail interface

## Implementation Stubs Requiring Attention

### Service Layer
21. **Server Service Implementations**
    - `GetServerSizeAsync()` in ServerService returns placeholder
    - **Needed**: Actual database size calculation

### Testing Infrastructure
22. **Integration Test Improvements**
    - Tests exist but could be enhanced with:
    - Real protocol compliance testing
    - Load testing capabilities
    - Performance benchmarks

## Configuration & Deployment

### Production Readiness
23. **Configuration Validation**
    - Basic configuration exists
    - **Needed**: Startup validation of critical settings

24. **Logging Enhancements**
    - Serilog configured but could be enhanced with:
    - Structured logging for better monitoring
    - Performance counters

25. **Docker & Deployment**
    - **Future**: Official Docker images
    - **Future**: Kubernetes manifests
    - **Future**: Automated deployment scripts

## Documentation TODOs

### API Documentation
26. **OpenAPI Enhancements**
    - Basic Swagger configured
    - **Needed**: Complete example requests/responses
    - **Needed**: Authentication flow documentation

### Protocol Documentation
27. **IMAP/SMTP/POP3 Compliance**
    - **Needed**: RFC compliance documentation
    - **Needed**: Supported extensions list
    - **Needed**: Client compatibility matrix

## Quality Assurance

### Code Coverage
28. **Test Coverage Improvements**
    - Current coverage: ~63.4%
    - **Target**: 90% for critical business logic
    - **Needed**: More edge case testing

### Performance Testing
29. **Load Testing**
    - **Needed**: Concurrent connection testing
    - **Needed**: Message throughput benchmarks
    - **Needed**: Memory usage under load

### Security Auditing
30. **Security Review**
    - **Needed**: Penetration testing
    - **Needed**: Dependency vulnerability scanning
    - **Needed**: Protocol security validation

## Priority Classification

### P0 (Critical - Blocks Production)
- Server backup/restore operations (#1, #2)
- IMAP FETCH/SEARCH commands (#7)

### P1 (High - Important for Production)
- Domain statistics and validation (#4, #6)
- System monitoring enhancements (#3)
- Message threading (#9)

### P2 (Medium - Nice to Have)
- Import/export operations (#5)
- Enhanced user statistics (#10)
- Configuration validation (#23)

### P3 (Low - Future Enhancements)
- All Phase 2 features (#11-20)
- Documentation improvements (#26-27)
- Advanced testing (#28-30)

---

*Last Updated: January 2024*
*Total Items: 30 outstanding items*
*Critical Items: 4*
*High Priority Items: 6*
