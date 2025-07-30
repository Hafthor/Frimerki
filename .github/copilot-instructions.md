# Copilot Instructions for Frímerki Email Server

## Project Overview
Frímerki is a lightweight, self-contained email server built in C# .NET 8 with SQLite as the primary data store. The focus is on minimal dependencies, clean architecture, and efficient operation on minimal hardware.

## Code Style & Formatting

### Brace Style
- **Use K&R (Kernighan & Ritchie) brace style consistently**
- Opening braces on the same line as the statement
- Closing braces on their own line, aligned with the statement
```csharp
public class Example {
    public void Method() {
        if (condition) {
            // code here
        }
    }
}
```

### General Preferences
- **Brevity is preferred** - Write concise code without sacrificing clarity
- Avoid verbose constructs when shorter, clear alternatives exist
- Use expression-bodied members where appropriate
- **Prefer explicit type with target-typed new** - Use `TypeName variable = new();` over `var variable = new TypeName();`
- **Prefer collection expressions for empty collections** - Use `List<T> variable = [];` over `List<T> variable = new();`
- Prefer `var` when the type is obvious from context (assignments from method calls, literals)
- Use meaningful but concise variable and method names
- **Prefer range indexers over Substring/Slice** - Use `[start..end]` syntax for string slicing
- **Prefer "" over string.Empty** - Use `""` for empty strings
- **Prefer ++ and -- over += 1 and -= 1** - Use `counter++` instead of `counter += 1`

### Formatting Examples
```csharp
// Preferred - explicit type with target-typed new
List<string> items = [];
StringBuilder builder = new();
Dictionary<string, int> counts = [];

// Preferred when type is obvious from context
var domain = await _context.Domains.FirstOrDefaultAsync();
var result = GetDomainAsync(name);
var count = 42;

// Avoid - verbose type repetition
List<string> items = new List<string>();
StringBuilder builder = new StringBuilder();

// Avoid - when collection expressions are available
List<string> items = new();

// Preferred - concise and clear
public async Task<DomainResponse> GetDomainAsync(string name) =>
    await _context.Domains
        .Where(d => d.Name == name)
        .Select(d => new DomainResponse {
            Id = d.Id,
            Name = d.Name,
            IsActive = d.IsActive
        })
        .FirstOrDefaultAsync();

// Avoid - unnecessarily verbose
public async Task<DomainResponse> GetDomainByNameAsync(string domainName) {
    var domain = await _context.Domains
        .Where(domain => domain.Name == domainName)
        .FirstOrDefaultAsync();

    if (domain != null) {
        return new DomainResponse {
            Id = domain.Id,
            Name = domain.Name,
            IsActive = domain.IsActive
        };
    }
    return null;
}

// Preferred - range indexers for string slicing
var content = message[(headerEnd + 2)..];  // Skip headers and get body
var size = literal[1..^1];                 // Remove surrounding braces
var prefix = line[..colonIndex];           // Get everything before colon

// Avoid - Substring/Slice methods
var content = message.Substring(headerEnd + 2);
var size = literal.Substring(1, literal.Length - 2);
var prefix = line.Substring(0, colonIndex);

// Preferred - concise increment/decrement
folder.Exists++;
folder.Recent++;
counter--;

// Avoid - verbose increment/decrement
folder.Exists += 1;
folder.Recent += 1;
counter -= 1;
```

## Architecture Patterns

### Service Layer Pattern
- Keep controllers thin - delegate business logic to services
- Services should be focused and have single responsibilities
- Use dependency injection for service registration

```csharp
// Controllers - thin and focused
[ApiController]
[Route("api/[controller]")]
public class DomainsController : ControllerBase {
    private readonly DomainService _domainService;

    public DomainsController(DomainService domainService) {
        _domainService = domainService;
    }

    [HttpGet]
    public async Task<ActionResult<List<DomainResponse>>> GetDomains() =>
        Ok(await _domainService.GetDomainsAsync());
}

// Services - contain business logic
public class DomainService {
    private readonly EmailDbContext _context;

    public async Task<List<DomainResponse>> GetDomainsAsync() {
        // Business logic here
    }
}
```

### DTO Pattern
- Use separate DTOs for requests and responses
- Keep DTOs simple and focused
- Include validation attributes on request DTOs
- Use descriptive but concise property names
- Avoid "Smurf-naming" (e.g. `MessageId` instead the `Message` class. Use `Id` instead.)

```csharp
public class DomainRequest {
    [Required, StringLength(255)]
    public string Name { get; set; }

    public bool IsActive { get; set; } = true;
}

public class DomainResponse {
    public int Id { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
    public int UserCount { get; set; }
    public long StorageUsed { get; set; }
}
```

## API Design Principles

### RESTful Conventions
- Use standard HTTP verbs (GET, POST, PUT, DELETE, PATCH)
- Design resources around nouns, not verbs
- Use HTTP status codes appropriately
- Prefer simple, consolidated endpoints over multiple specialized ones

### Error Handling
- Return consistent error responses
- Use appropriate HTTP status codes
- Include helpful error messages
- Handle exceptions gracefully

```csharp
[HttpGet("{name}")]
public async Task<ActionResult<DomainResponse>> GetDomain(string name) {
    var domain = await _domainService.GetDomainAsync(name);
    return domain == null ? NotFound() : Ok(domain);
}
```

## Database & Entity Framework

### Entity Configuration
- Use fluent API for complex configurations
- Keep entity classes simple
- Use appropriate data annotations for simple constraints

### Query Patterns
- Use async/await consistently
- Prefer LINQ method syntax over query syntax
- Project to DTOs in queries when possible
- Use `FirstOrDefaultAsync()` instead of `SingleOrDefaultAsync()` unless uniqueness is critical
- Prefer .Order over .OrderBy when you were going to order by the element itself, i.e. `list.Order(x => x)`

```csharp
public async Task<DomainResponse> GetDomainAsync(string name) =>
    await _context.Domains
        .Where(d => d.Name == name)
        .Select(d => new DomainResponse {
            Id = d.Id,
            Name = d.Name,
            IsActive = d.IsActive,
            UserCount = d.Users.Count()
        })
        .FirstOrDefaultAsync();
```

## Testing & Validation

### Input Validation
- Use data annotations for basic validation
- Implement custom validation for complex business rules
- Validate at the DTO level, not in controllers

### Error Responses
- Return structured error responses
- Use consistent error formats across the API
- Include correlation IDs for debugging

## Security Considerations

### Authentication & Authorization
- Use JWT tokens for API authentication
- Implement role-based authorization (User, DomainAdmin, HostAdmin)
- Validate permissions at the service level
- Use `[Authorize]` attributes appropriately

### Input Sanitization
- Always validate and sanitize user input
- Use parameterized queries (Entity Framework handles this)
- Escape output appropriately

## Performance Guidelines

### Database Optimization
- Use appropriate indexes
- Avoid N+1 query problems
- Project to DTOs to avoid loading unnecessary data
- Use `AsNoTracking()` for read-only queries

### Caching Strategy
- Cache frequently accessed, slowly changing data
- Use appropriate cache expiration policies
- Consider memory usage in caching decisions

## Documentation & Comments

### Code Documentation
- Use XML documentation for public APIs
- Keep comments concise and focused on "why" not "what"
- Avoid obvious comments
- Document complex business logic

### API Documentation
- Use clear, descriptive endpoint summaries
- Document required parameters and expected responses
- Include example requests/responses where helpful

## Naming Conventions

### General Guidelines
- Use PascalCase for classes, methods, properties
- Use camelCase for parameters, local variables
- Use descriptive but concise names
- Avoid abbreviations unless they're well-known (like DTO, API, HTTP)

### Specific Patterns
- Controllers: `{Resource}Controller` (e.g., `DomainsController`)
- Services: `{Resource}Service` (e.g., `DomainService`)
- DTOs: `{Resource}Request/Response` (e.g., `DomainRequest`, `DomainResponse`)
- Entities: `{Resource}` (e.g., `Domain`, `User`)

## Project Structure

### Folder Organization
```
/Controllers     - API controllers (thin)
/Services        - Business logic services
/DTOs           - Data transfer objects
/Entities       - Entity Framework entities
/Data           - DbContext and configurations
/Extensions     - Extension methods and utilities
```

### File Naming
- One class per file
- File name matches class name
- Group related classes in appropriate folders

## Dependencies & Libraries

### Preferred Libraries
- **Entity Framework Core** - ORM and database access
- **AutoMapper** - Object-to-object mapping (when needed)
- **FluentValidation** - Complex validation scenarios
- **Serilog** - Structured logging
- **System.Text.Json** - JSON serialization (built-in)

### Dependency Management
- Keep dependencies minimal
- Prefer built-in .NET functionality when possible
- Regular dependency updates for security
- Avoid unnecessary abstractions

## Common Patterns to Follow

### Service Registration
```csharp
// In ServiceCollectionExtensions.cs
public static IServiceCollection AddApplicationServices(this IServiceCollection services) {
    services.AddScoped<DomainService>();
    services.AddScoped<ServerService>();
    return services;
}
```

### Controller Action Patterns
```csharp
// GET single resource
[HttpGet("{id}")]
public async Task<ActionResult<ResourceResponse>> GetResource(int id) {
    var resource = await _service.GetResourceAsync(id);
    return resource == null ? NotFound() : Ok(resource);
}

// POST new resource
[HttpPost]
public async Task<ActionResult<ResourceResponse>> CreateResource(ResourceRequest request) {
    var resource = await _service.CreateResourceAsync(request);
    return CreatedAtAction(nameof(GetResource), new { id = resource.Id }, resource);
}
```

## What to Avoid

- Overly complex inheritance hierarchies
- Unnecessary abstractions and interfaces
- Verbose error handling that obscures the main logic
- Large methods that do multiple things
- Magic strings and numbers (use constants)
- Exposing Entity Framework entities directly in APIs
- Synchronous database operations
- Catching and rethrowing exceptions without adding value

## Email Server Specific Guidelines

### IMAP/SMTP Protocol Implementation
- Follow RFC specifications closely
- Handle protocol errors gracefully
- Use appropriate logging for protocol violations
- Implement proper connection management

### Security
- Validate email addresses and domain names
- Implement proper authentication for all protocols
- Use secure defaults (require TLS, strong passwords)
- Log security events appropriately

### Performance
- Optimize for low memory usage
- Handle concurrent connections efficiently
- Implement appropriate timeouts
- Monitor resource usage

Remember: **Clarity over cleverness, brevity over verbosity, simplicity over complexity.**
