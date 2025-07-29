# Code Formatting Guide

This project uses `dotnet format` to maintain consistent code formatting across the codebase.

## Automatic Formatting Checks

### Pre-commit Hook
A git pre-commit hook is installed that will automatically check code formatting before each commit. If your code is not properly formatted, the commit will be rejected with instructions on how to fix it.

**If your commit is rejected:**
1. Run `dotnet format` to fix formatting issues
2. Stage the formatted changes: `git add .`
3. Commit again: `git commit`

### Manual Tools

#### Quick Format
To format your code manually:
```bash
dotnet format
```

#### Comprehensive Check
To format, build, and test in one command:
```bash
./scripts/format-and-check.sh
```

This script will:
1. ✅ Format all C# code
2. ✅ Verify the build still works
3. ✅ Run all tests to ensure functionality is preserved

## VS Code Integration

For the best development experience in VS Code:

1. **Enable Format on Save:**
   - Open VS Code settings (Cmd/Ctrl + ,)
   - Search for "format on save"
   - Enable "Editor: Format On Save"

2. **Enable Format on Paste:**
   - Search for "format on paste"
   - Enable "Editor: Format On Paste"

## Formatting Rules

The project follows standard .NET formatting conventions with these preferences from the coding guidelines:

- **K&R brace style** (opening braces on same line)
- **Alphabetical using statements** (System → Project → Third-party → Microsoft)
- **Target-typed new expressions** where appropriate
- **Collection expressions** for empty collections
- **Range indexers** for string slicing

## Bypassing the Hook (Emergency Only)

In rare cases where you need to commit without formatting checks:
```bash
git commit --no-verify
```

**⚠️ Use sparingly!** Only use this for emergency hotfixes or when the formatting tool has issues.

## CI/CD Integration

Consider adding this check to your continuous integration pipeline:
```yaml
- name: Check code formatting
  run: dotnet format --verify-no-changes
```

This ensures that all code merged to main branches maintains consistent formatting.
