# Browser Reporter Service

A Windows system tray application that silently collects browsing history from Chrome and Edge browsers and reports it to a central server based on dynamic configuration. Designed for enterprise deployment with automatic startup for all users.

## Features

- **Silent Operation**: Runs in system tray with minimal user interaction
- **Dynamic Configuration**: Downloads encrypted configuration from server
- **Browser Support**: Chrome and Edge history collection
- **Enterprise Ready**: MSI installer with all-users startup
- **Deduplication**: Prevents duplicate reporting with SQLite cache
- **LDAP Integration**: User authorization via Active Directory groups
- **Rolling Logs**: Configurable log rotation and size limits
- **AES Encryption**: Secure configuration with 256-bit encryption

## System Requirements

- **OS**: Windows 10/11 or Windows Server 2019/2022
- **Framework**: .NET 8 Runtime (included in self-contained deployment)
- **Architecture**: x64
- **Permissions**: User-level access for browser history, admin for installation

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
- **Deployment**: Installs to `C:\Program Files\BrowserReporterService\`
- **Startup**: Creates shortcut in all-users startup folder

## Deployment

### Enterprise Deployment (Recommended)

1. **MSI Installation**: Deploy via Configuration Manager, Group Policy, or manual installation
   ```cmd
   msiexec /i BrowserReporterService.msi ALLUSERS=1
   ```

2. **Automatic Startup**: The app automatically starts for all users via the all-users startup folder
3. **Configuration**: App downloads encrypted configuration from server on first run

### Manual Installation

1. Extract the self-contained folder to `C:\Program Files\BrowserReporterService\`
2. Create shortcut in `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\`
3. Run `BrowserReporterService.exe` to start the application

## Configuration

The application downloads an AES-encrypted configuration file from the server. Configuration includes:

```json
{
  "server_url": "https://your-server.com",
  "api_key": "your-api-key",
  "sync_interval_minutes": 5,
  "retry_interval_seconds": 300,
  "max_history_age_hours": 720,
  "ldap": {
    "server": "ldap://your-domain.com",
    "base_dn": "DC=yourdomain,DC=com"
  },
  "enable_group_filtering": true,
  "security_groups": ["Group1", "Group2"],
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
| `--encryptconfig` | Encrypt a plaintext config file |

### Examples

```bash
# Debug mode with console output
BrowserReporterService.exe --debug

# Single sync cycle for testing
BrowserReporterService.exe --once

# Use local config file
BrowserReporterService.exe --config "C:\temp\config.json"

# Encrypt configuration
BrowserReporterService.exe --encryptconfig --config "plaintext.json"
```

## System Tray Interface

The application runs in the system tray with status indicators:

- **Green Icon**: Connected and reporting successfully
- **Yellow Icon**: Connected but user not authorized
- **Red Icon**: Error (config, API, or sync failure)
- **Grey Icon**: Syncing in progress

### Context Menu Options

- **Force Data Sync Now**: Manually trigger a sync cycle
- **View Logs**: Open log file in default text editor
- **Exit**: Close the application

## Logging

Logs are stored in `%LOCALAPPDATA%\BrowserReporter\logs.txt` with:
- Rolling file size (configurable, default 5MB)
- Retention count (configurable, default 3 files)
- Daily rotation as backup

## Security

- **AES-256-CBC**: Configuration encryption
- **LDAP Authentication**: User authorization via AD groups
- **API Key Authentication**: Server communication security
- **Local Cache**: SQLite database for deduplication
- **No Admin Rights**: Runs with user-level permissions

## Troubleshooting

### Common Issues

1. **App doesn't start**: Check if .NET 8 runtime is installed
2. **No data reported**: Verify user is in authorized AD groups
3. **Configuration errors**: Check server URL and API key
4. **SQLite errors**: Ensure write permissions to app data folder

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
- All-users startup configuration
- LDAP integration
- AES configuration encryption
- SQLite deduplication cache
- Rolling log system

## License

[Add your license information here]

## Support

[Add your support contact information here] 