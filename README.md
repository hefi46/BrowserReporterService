# Browser Reporter Service

A headless Windows application that silently collects browsing history from Chrome and Edge browsers and reports it to a central server based on dynamic configuration. Designed for enterprise deployment via Group Policy.

## Features

- **Silent Operation**: Runs headless with no user-visible UI
- **Dynamic Configuration**: Downloads encrypted configuration from server
- **Browser Support**: Chrome and Edge history collection
- **Enterprise Ready**: MSI installer + Group Policy deployment
- **Deduplication**: Prevents duplicate reporting with SQLite cache
- **LDAP Integration**: User authorization via Active Directory groups
- **Rolling Logs**: Configurable log rotation and size limits
- **AES Encryption**: Secure configuration with 256-bit encryption

## System Requirements

- **OS**: Windows 10/11 or Windows Server 2019/2022
- **Framework**: None required (self-contained deployment includes .NET 8 runtime)
- **Architecture**: x64
- **Permissions**: User-level access for browser history, admin for MSI installation

## Development Environment

- **Framework**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **IDE**: Visual Studio 2022 or Visual Studio Code with C# extensions
- **Build Tools**: WiX Toolset v6 for MSI creation

## Building the Application

### Self-Contained Folder Deployment (Recommended)

```bash
# Build the application
dotnet publish -c Release -r win-x64 --self-contained true

# Build the MSI installer
dotnet build Installer/BrowserReporterInstaller.wixproj -c Release
```

The MSI installer will be created at `Installer/bin/Release/BrowserReporterService.msi`

### Build Output

- **Application**: Self-contained folder with all dependencies
- **Installer**: MSI package for enterprise deployment
- **Install Location**: `C:\Program Files\BrowserReporterService\`
- **Install Location**: `C:\Program Files\BrowserReporterService\`

## Deployment

### Group Policy Deployment (Recommended)

The recommended enterprise deployment uses **Group Policy** with two components:

1. **MSI Installation** (Computer Configuration GPO)
   - Deploy via Group Policy Software Installation
   - Installs to `C:\Program Files\BrowserReporterService\`
   - Does NOT create automatic startup shortcuts

   ```cmd
   # Manual installation alternative
   msiexec /i BrowserReporterService.msi /quiet
   ```

2. **Logon Script** (User Configuration GPO)
   - Launch the application at user login using a PowerShell or batch script
   - Allows custom command-line flags
   - Enables different configurations per user group

**See `DEPLOYMENT.md` for complete step-by-step Group Policy configuration instructions.**

### Quick Start Example

```powershell
# PowerShell logon script for deployment
Start-Process "C:\Program Files\BrowserReporterService\BrowserReporterService.exe" -WindowStyle Hidden
```

## Configuration

The application downloads an AES-encrypted configuration file from the server. Configuration includes:

```json
{
  "server_url": "https://your-server.com",
  "sync_interval_minutes": 5,
  "max_history_age_hours": 24,
  "monitored_users_group": "",
  "monitored_users": [],
  "monitored_hours": {
    "start": "00:00",
    "end": "23:59"
  },
  "browsers": ["chrome", "edge"],
  "log_max_mb": 5,
  "log_roll_count": 3
}
```

### Configuration Encryption

To encrypt a configuration file:

```bash
BrowserReporterService.exe --encryptconfig --config "plaintext-config.json"
```

This outputs an encrypted envelope that can be deployed to the server.

## Command-Line Options

| Option | Description |
|--------|-------------|
| `--debug` | Enable console output for debugging |
| `--once` | Perform single sync cycle and exit |
| `--config <path>` | Use local plaintext config file |
| `--server <url>` | Override server URL from config |
| `--encryptconfig` | Encrypt a plaintext config file (requires `--config`) |
| `--install` | Register a Windows scheduled task for auto-start |
| `--uninstall` | Remove the Windows scheduled task |

### Examples

```bash
# Debug mode with console output
BrowserReporterService.exe --debug

# Single sync cycle for testing
BrowserReporterService.exe --once

# Use local config file
BrowserReporterService.exe --config "C:\temp\config.json"

# Headless deployment with custom config
BrowserReporterService.exe --config "\\server\share\config.json"

# Encrypt configuration
BrowserReporterService.exe --encryptconfig --config "plaintext.json"
```

## Logging

Logs are stored in `%LOCALAPPDATA%\BrowserReporter\logs.txt` with:
- Rolling file size (configurable, default 5MB)
- Retention count (configurable, default 3 files)
- Daily rotation as backup

## Security

- **AES-256-CBC**: Configuration encryption
- **LDAP Authentication**: User authorization via AD groups
- **Local Cache**: SQLite database for deduplication
- **No Admin Rights**: Runs with user-level permissions

## Troubleshooting

### Common Issues

1. **App doesn't start**: Check Windows Event Viewer for application errors, verify installation completed successfully
2. **No data reported**: Verify user is in authorized AD groups and within monitoring hours
3. **Configuration errors**: Check server URL is accessible and encrypted config is valid
4. **SQLite errors**: Ensure write permissions to `%LOCALAPPDATA%\BrowserReporter\`

### Debug Mode

Run with `--debug` flag to see detailed console output:

```bash
BrowserReporterService.exe --debug
```

### Log Analysis

Logs contain detailed information about:
- Configuration loading
- Browser scanning
- API communication
- Authorization checks
- Error details

## Version History

### v1.0.0
- Initial release
- Chrome and Edge browser support
- Enterprise MSI deployment
- Group Policy deployment support
- Headless operation (no tray icon)
- LDAP integration with AD groups
- AES configuration encryption
- SQLite deduplication cache
- Rolling log system
- Self-contained deployment (no runtime dependencies)

## License

[Add your license information here]

## Support

[Add your support contact information here] 