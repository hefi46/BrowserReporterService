# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Browser Reporter Service is a Windows system tray application that collects browsing history from Chrome and Edge browsers and reports it to a central server. It's designed for enterprise deployment with MSI installer and Group Policy-based startup configuration.

**Technology Stack:**
- .NET 8 (Windows Forms, self-contained deployment)
- WiX Toolset v6 for MSI installer
- SQLite for local caching
- Serilog for logging
- Newtonsoft.Json for serialization

## Build Commands

### Development Build
```bash
# Build the application (debug)
dotnet build

# Build the application (release)
dotnet build -c Release

# Run the application with debug console
dotnet run -- --debug

# Run single sync cycle for testing
dotnet run -- --once
```

### Production Deployment
```bash
# Publish self-contained folder deployment
dotnet publish -c Release -r win-x64 --self-contained true

# Build MSI installer (requires WiX Toolset v6)
dotnet build Installer/BrowserReporterInstaller.wixproj -c Release
```

**Output Locations:**
- Published app: `bin\Release\net8.0-windows\win-x64\publish\`
- MSI installer: `Installer\bin\Release\BrowserReporterService.msi`

### Configuration Encryption
```bash
# Encrypt a plaintext config file
dotnet run -- --encryptconfig --config "path\to\plaintext-config.json"
```

### Command-Line Flags
```bash
# Run without system tray icon (recommended for Group Policy deployment)
dotnet run -- --no-tray

# Combine flags for headless deployment with custom config
dotnet run -- --no-tray --config "path\to\config.json"

# Test mode - single sync then exit
dotnet run -- --once --debug
```

## Deployment Strategy

The recommended enterprise deployment approach uses **Group Policy** with two components:

1. **MSI Installation** (Computer Configuration GPO)
   - Installs the application to `C:\Program Files\BrowserReporterService\`
   - Does NOT create automatic startup shortcuts
   - Deployed via Software Installation policy

2. **Logon Script** (User Configuration GPO)
   - Launches the application at user login
   - Allows custom command-line flags (e.g., `--no-tray`)
   - Enables different configurations per user group
   - PowerShell script deployed via SYSVOL

**Advantages:**
- Centralized control of installation and startup
- Easy to update startup parameters without reinstalling
- Per-group customization (different flags for different departments)
- No persistent system tray icon with `--no-tray` flag
- Clean separation of installation and execution policies

**See `DEPLOYMENT.md` for complete step-by-step instructions.**

## Architecture

### Entry Point and Application Lifecycle

**Program.cs**: Main entry point that handles:
1. SQLite provider initialization (`Batteries.Init()`)
2. Command-line argument parsing via `CommandLineArgs`
3. Console allocation for debug mode
4. Logging setup through `LoggingService`
5. Routing to either utility commands or main tray application

**TrayApplicationContext**: Core application context that:
- Manages system tray icon with 4 states (green/yellow/red/grey)
- Downloads and decrypts encrypted configuration from server
- Schedules periodic sync cycles with jitter (+/- 30% randomization)
- Orchestrates the sync pipeline: authorization → browser scan → cache filtering → API send → icon update
- Provides password-protected exit functionality

### Service Architecture

All services live in the `Services/` directory:

**ConfigService**: Configuration management
- Downloads encrypted config from `{server_url}/secureconfig.json`
- Falls back to bootstrap config at `%ProgramData%\BrowserReporter\bootstrap.json`
- Hardcoded DNS fallback: `http://browserreporter:8000`
- Supports local plaintext config via `--config` flag

**CryptoService**: AES-256-CBC encryption/decryption
- Uses hardcoded key derived from `SHA256("BrowserReporter2024!MasterKey")`
- Implements secure envelope format with IV, checksum, and optional HMAC signature
- Validates integrity via SHA256 checksum and HMAC-SHA256 signature

**BrowserScannerService**: Browser history scanning
- Queries Chrome/Edge SQLite history databases in parallel
- Supports multiple profiles (Default, Profile 1, Profile 2, etc.)
- Converts WebKit timestamps (microseconds since 1601-01-01) to Unix milliseconds
- Uses immutable read-only mode first, falls back to temp copy if database is locked
- Only scans history from last 24 hours (hardcoded cutoff)

**CacheService**: SQLite-based deduplication
- Stores sent items by composite key: `{Url}:{VisitTime}`
- Database location: `%LOCALAPPDATA%\BrowserReporter\sent_cache.db`
- Single table schema: `sent_items(id TEXT PRIMARY KEY, sent_at TEXT)`
- Filters out previously sent items before API submission

**AuthorizationService**: LDAP/AD integration
- Validates user membership in configured AD groups
- Checks monitoring time windows (start/end hours)
- Determines which browsers to monitor based on config

**ApiClient**: HTTP communication with server
- Sends batches of 500 visits per request
- POST to `{server_url}/api/reports`
- Includes user info (username, domain, groups, department, etc.) with each batch
- Stops on first failed batch to preserve data integrity

**UserInfoService**: Active Directory user info retrieval
- Queries AD for user details (full name, email, department, groups)
- Provides comprehensive user context for each report

**LoggingService**: Serilog configuration
- Log location: `%LOCALAPPDATA%\BrowserReporter\logs.txt`
- Rolling file size (default 5MB) with retention count (default 3 files)
- Console output in debug mode
- Dynamic logger reconfiguration after config download

### Data Models

**ConfigModels.cs**:
- `SecureConfigEnvelope`: Encrypted config wrapper (version, encrypted_data, iv, checksum, signature)
- `AppConfig`: Plaintext configuration with all runtime settings
- `MonitoredHoursConfig`: Time window for monitoring (start/end)

**DataModels.cs**:
- `BrowserVisit`: Single history entry with URL, title, visit time, browser type, profile
- `UserInfo`: AD user details (username, domain, full name, email, department, groups)
- `ReportPayload`: Batch payload sent to server (username, visits array, user info)

### Sync Pipeline Flow

1. **Timer trigger** with jitter (±30% of sync interval)
2. **Authorization check**: Verify user in monitored group and within time window
3. **Browser scan**: Query Chrome/Edge history databases in parallel
4. **Cache filtering**: Load sent items from SQLite, filter out duplicates
5. **Batch sending**: Send 500 items at a time to API
6. **Cache update**: Mark successfully sent items in SQLite
7. **Icon update**: Set tray icon color based on outcome (green/yellow/red)
8. **Reschedule**: Calculate next sync time with new jitter

### Icon States

- **Green**: Connected and reporting successfully
- **Yellow**: Connected but user not authorized or outside monitoring hours
- **Red**: Error occurred (config failed, API error, sync failed)
- **Grey**: Currently syncing or initializing

## Important Implementation Details

### Browser Database Access
The `BrowserScannerService` handles locked databases through a two-step approach:
1. First attempt: Open with immutable read-only mode using URI format `file:{path}?mode=ro&immutable=1`
2. Fallback: Copy database + WAL + SHM files to temp location and query the copy
3. Always clean up temp files in finally block

### Security Considerations
- AES encryption key is hardcoded in `CryptoService.cs` (line 9)
- Exit password defaults to `BRAdmin2025` but can be configured
- Bootstrap config contains server URL and may be deployed to `%ProgramData%\BrowserReporter\`
- Local plaintext configs (via `--config`) bypass all encryption

### Timestamp Conversion
Chrome/Edge use WebKit timestamps:
- Microseconds since January 1, 1601 UTC
- Convert to Unix milliseconds: `(webkitTime / 1000) - 11644473600000`
- Constant `WebkitEpochDelta = 11644473600000` in BrowserScannerService.cs:15

### Installer Details
- WiX 6.0 project harvests entire publish folder
- Installs to `C:\Program Files\BrowserReporterService\`
- Does NOT create automatic startup shortcuts (designed for Group Policy deployment)
- Single-file deployment disabled (`PublishSingleFile=false`) for better compatibility
- See `DEPLOYMENT.md` for complete Group Policy deployment guide

## Testing and Debugging

### Debug Mode
```bash
# Run with console output
BrowserReporterService.exe --debug

# Single sync cycle (exits after one run)
BrowserReporterService.exe --once

# Use local config file (bypasses server download)
BrowserReporterService.exe --config "C:\path\to\config.json"

# Run without tray icon (headless mode for GPO deployment)
BrowserReporterService.exe --no-tray

# Combine flags
BrowserReporterService.exe --no-tray --config "\\server\share\config.json"
```

### Show Debug Console at Runtime
Right-click tray icon → "Show Debug Console" to allocate console window for live logging.

### Log Files
Logs are in `%LOCALAPPDATA%\BrowserReporter\logs.txt` with automatic rotation based on configured limits.

## Configuration Schema

Required fields in AppConfig:
- `server_url`: Server endpoint for config download and report submission
- `sync_interval_minutes`: How often to sync (default: 5)
- `max_history_age_hours`: History cutoff (currently hardcoded to 24 hours in code)
- `monitored_users_group`: AD group name for authorization
- `monitored_users`: List of specific usernames to monitor
- `monitored_hours`: Time window object with `start` and `end` (HH:mm format)
- `browsers`: Array of browsers to monitor (e.g., ["chrome", "edge"])
- `log_max_mb`: Max log file size before rotation (default: 5)
- `log_roll_count`: Number of old log files to keep (default: 3)
- `exit_password`: Password required to exit the application (default: "BRAdmin2025")

## Common Development Tasks

### Adding a New Browser
1. Add browser name to config schema in `ConfigModels.cs`
2. Add case in `BrowserScannerService.ScanAllBrowsersAsync()` with profile path
3. Ensure WebKit timestamp conversion is compatible (may differ for non-Chromium browsers)

### Modifying Sync Interval Logic
- Jitter calculation: `Program.cs` line 242 in `ScheduleNextSync()`
- Change jitter range by modifying the multiplier (currently ±30%)

### Changing Batch Size
- Batch size constant: `Program.cs` line 344 in `SendVisitsInBatches()`
- Default: 500 items per batch

### Updating Encryption
- Encryption key: `CryptoService.cs` line 9
- Algorithm: AES-256-CBC with PKCS7 padding
- Modify `EncryptConfig()` and `DecryptConfig()` methods to change encryption scheme
