# Browser Reporter Service - Group Policy Deployment Guide

## Overview

This guide covers deploying Browser Reporter Service via Active Directory Group Policy, allowing centralized control of installation and startup behavior across your organization.

## Prerequisites

- Active Directory Domain Services (AD DS)
- Group Policy Management Console (GPMC)
- Administrative access to create/modify GPOs
- Network share accessible by target computers for MSI distribution

## Deployment Strategy

The recommended deployment approach uses two separate GPOs:

1. **Software Installation GPO** - Installs the MSI package
2. **Startup Script GPO** - Launches the application with custom flags at user login

This separation allows you to:
- Control which computers get the software installed
- Control which users have the application auto-start
- Specify different command-line flags per user group
- Update startup parameters without reinstalling

## Part 1: MSI Installation via Group Policy

### Step 1: Prepare the MSI Package

1. Build the MSI installer:
   ```powershell
   dotnet publish -c Release -r win-x64 --self-contained true
   dotnet build Installer/BrowserReporterInstaller.wixproj -c Release
   ```

2. Copy the MSI to a network share:
   ```powershell
   # Create/use a share accessible by DOMAIN\Domain Computers
   # Example: \\fileserver\software\BrowserReporter\
   copy Installer\bin\Release\BrowserReporterService.msi \\fileserver\software\BrowserReporter\
   ```

3. Set NTFS and Share permissions:
   - **DOMAIN\Domain Computers**: Read
   - **DOMAIN\Domain Admins**: Full Control

### Step 2: Create Software Installation GPO

1. Open **Group Policy Management Console** (gpmc.msc)

2. Right-click your target OU → **Create a GPO in this domain, and Link it here**
   - Name: `Install - Browser Reporter Service`

3. Right-click the new GPO → **Edit**

4. Navigate to:
   ```
   Computer Configuration
     → Policies
       → Software Settings
         → Software Installation
   ```

5. Right-click **Software Installation** → **New** → **Package**

6. Browse to: `\\fileserver\software\BrowserReporter\BrowserReporterService.msi`
   - **Important**: Use UNC path, not mapped drive

7. In the deployment dialog:
   - Select **Assigned**
   - Click **OK**

8. Right-click the package → **Properties**:
   - **Deployment** tab:
     - ☑ Uninstall this application when it falls out of the scope of management
     - ☐ Do not display this package in Add/Remove Programs (optional - check for stealth deployment)
   - **Upgrades** tab: Configure automatic upgrades when you release new versions

### Step 3: Deploy and Verify

1. Run `gpupdate /force` on a test machine (or wait for automatic refresh)
2. Reboot the test machine
3. Verify installation at: `C:\Program Files\BrowserReporterService\`

## Part 2: Auto-Start via Logon Script GPO

### Step 1: Create the Logon Script

Create a PowerShell script to launch the application with your desired flags:

**Option A: Basic Launch (with tray icon)**
```powershell
# StartBrowserReporter.ps1
$appPath = "C:\Program Files\BrowserReporterService\BrowserReporterService.exe"
if (Test-Path $appPath) {
    Start-Process $appPath -WindowStyle Hidden
}
```

**Option B: Headless Launch (no tray icon)**
```powershell
# StartBrowserReporterHeadless.ps1
$appPath = "C:\Program Files\BrowserReporterService\BrowserReporterService.exe"
if (Test-Path $appPath) {
    Start-Process $appPath -ArgumentList "--no-tray" -WindowStyle Hidden
}
```

**Option C: Custom Configuration**
```powershell
# StartBrowserReporterCustom.ps1
$appPath = "C:\Program Files\BrowserReporterService\BrowserReporterService.exe"
$configPath = "\\fileserver\configs\BrowserReporter\config.json"
if (Test-Path $appPath) {
    Start-Process $appPath -ArgumentList "--no-tray --config `"$configPath`"" -WindowStyle Hidden
}
```

**Option D: Advanced with Logging**
```powershell
# StartBrowserReporterAdvanced.ps1
$appPath = "C:\Program Files\BrowserReporterService\BrowserReporterService.exe"
$logFile = "$env:TEMP\BrowserReporter_Launch.log"

try {
    if (Test-Path $appPath) {
        $args = @("--no-tray")

        # Optional: Add custom config from network share
        $networkConfig = "\\fileserver\configs\BrowserReporter\config.json"
        if (Test-Path $networkConfig) {
            $args += "--config"
            $args += $networkConfig
        }

        Start-Process $appPath -ArgumentList $args -WindowStyle Hidden
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - Successfully launched Browser Reporter Service" | Out-File $logFile -Append
    } else {
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - ERROR: Application not found at $appPath" | Out-File $logFile -Append
    }
} catch {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - ERROR: $($_.Exception.Message)" | Out-File $logFile -Append
}
```

### Step 2: Deploy the Script via Group Policy

1. Copy the script to the SYSVOL share:
   ```powershell
   copy StartBrowserReporter.ps1 "\\yourdomain.com\SYSVOL\yourdomain.com\scripts\"
   ```

2. Open **Group Policy Management Console**

3. Create a new GPO or edit existing:
   - Name: `Startup - Browser Reporter Service`
   - Link to the appropriate OU (Users)

4. Right-click the GPO → **Edit**

5. Navigate to:
   ```
   User Configuration
     → Policies
       → Windows Settings
         → Scripts (Logon/Logoff)
   ```

6. Double-click **Logon**

7. Click **PowerShell Scripts** tab

8. Click **Add** → **Browse**

9. Browse to: `\\yourdomain.com\SYSVOL\yourdomain.com\scripts\StartBrowserReporter.ps1`

10. Click **OK** to save

### Step 3: Configure PowerShell Execution Policy (if needed)

If PowerShell scripts are blocked, create a separate GPO:

1. Navigate to:
   ```
   User Configuration
     → Policies
       → Administrative Templates
         → Windows Components
           → Windows PowerShell
   ```

2. Enable **Turn on Script Execution**
   - Set to: **Allow local scripts and remote signed scripts**

### Step 4: Test the Deployment

1. Link the GPO to a test OU with a test user
2. Log in as the test user
3. Verify the application is running:
   ```powershell
   Get-Process BrowserReporterService -ErrorAction SilentlyContinue
   ```
4. Check logs at: `%LOCALAPPDATA%\BrowserReporter\logs.txt`

## Advanced Configuration Options

### Per-Group Configurations

Deploy different configurations to different user groups:

1. Create multiple logon scripts with different flags:
   - `StartBrowserReporter_Sales.ps1` → `--config \\server\configs\sales-config.json`
   - `StartBrowserReporter_IT.ps1` → `--config \\server\configs\it-config.json`

2. Create separate GPOs for each group

3. Use Security Filtering to target specific AD groups

### Item-Level Targeting

Use Group Policy Preferences with Item-Level Targeting for more granular control:

1. Navigate to:
   ```
   User Configuration
     → Preferences
       → Control Panel Settings
         → Scheduled Tasks
   ```

2. Create an **Immediate Task** that runs at logon

3. Configure Item-Level Targeting based on:
   - Security Group membership
   - OU membership
   - Computer name patterns
   - OS version
   - IP address range

### Preventing User Termination

To prevent users from easily closing the application:

1. Use the `--no-tray` flag (removes tray icon access)
2. Configure exit password in the config file
3. Remove Task Manager access via GPO (optional, not recommended for most environments)

### Monitoring Deployment

**Check installed version across domain:**
```powershell
# Query all computers in OU
Get-ADComputer -SearchBase "OU=Workstations,DC=domain,DC=com" -Filter * | ForEach-Object {
    $computer = $_.Name
    $version = Invoke-Command -ComputerName $computer -ScriptBlock {
        $path = "C:\Program Files\BrowserReporterService\BrowserReporterService.exe"
        if (Test-Path $path) {
            (Get-Item $path).VersionInfo.FileVersion
        }
    } -ErrorAction SilentlyContinue
    [PSCustomObject]@{
        Computer = $computer
        Version = $version
    }
}
```

**Check running instances:**
```powershell
Get-ADComputer -Filter * | ForEach-Object {
    $computer = $_.Name
    $running = Invoke-Command -ComputerName $computer -ScriptBlock {
        Get-Process BrowserReporterService -ErrorAction SilentlyContinue
    } -ErrorAction SilentlyContinue
    [PSCustomObject]@{
        Computer = $computer
        Running = ($null -ne $running)
    }
}
```

## Troubleshooting

### Application Not Installing

1. Check GPO application:
   ```cmd
   gpresult /r /scope:computer
   ```

2. Verify network share permissions:
   - Ensure DOMAIN\Domain Computers can read the MSI

3. Check event logs:
   - Event Viewer → Applications and Services Logs → Microsoft → Windows → GroupPolicy

### Application Not Starting at Logon

1. Check GPO application:
   ```cmd
   gpresult /r /scope:user
   ```

2. Verify script execution:
   ```powershell
   Get-ExecutionPolicy -List
   ```

3. Check logon script logs (if using Option D script above):
   ```cmd
   type %TEMP%\BrowserReporter_Launch.log
   ```

4. Manually test the script:
   ```powershell
   & "\\yourdomain.com\SYSVOL\yourdomain.com\scripts\StartBrowserReporter.ps1"
   ```

### Multiple Instances Running

If users log off/on frequently, you might get multiple instances:

**Prevention script** (add to the top of logon script):
```powershell
# Kill any existing instances before starting new one
Get-Process BrowserReporterService -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
```

### Config File Not Loading

1. Verify network path is accessible:
   ```powershell
   Test-Path "\\fileserver\configs\BrowserReporter\config.json"
   ```

2. Check NTFS permissions on config file

3. Verify config format is valid JSON

4. Check application logs for specific errors

## Command-Line Flags Reference

Available flags for customizing startup behavior:

- `--no-tray` - Run without system tray icon (recommended for GPO deployment)
- `--debug` - Enable debug console window (for troubleshooting)
- `--once` - Run single sync cycle and exit (for testing)
- `--config <path>` - Use custom config file (local or UNC path)
- `--server <url>` - Override server URL from config

**Example combinations:**
```powershell
# Production deployment - headless with custom config
BrowserReporterService.exe --no-tray --config "\\server\configs\config.json"

# Debug deployment - visible tray with debug console
BrowserReporterService.exe --debug

# Testing - single run without staying resident
BrowserReporterService.exe --once --debug
```

## Upgrading to New Versions

### Method 1: In-Place Upgrade via GPO

1. Build new MSI with updated version number in `Product.wxs`
2. Replace the MSI on the network share
3. In GPMC, right-click the software package → **All Tasks** → **Redeploy application**
4. Next reboot will install the upgrade

### Method 2: Side-by-Side Upgrade

1. Create new software package with new version
2. Configure the new package to upgrade the old one:
   - Properties → **Upgrades** tab
   - Add the old package
3. Deploy the new GPO

### Method 3: Uninstall and Reinstall

1. Remove the old GPO link
2. Allow automatic uninstall (if configured)
3. Link new GPO with updated MSI

**Note**: The UpgradeCode in `Product.wxs` must remain the same for automatic upgrades to work.

## Security Considerations

### Least Privilege Deployment

- Install software via **Computer Configuration** (admin rights)
- Launch via **User Configuration** (user rights)
- Application runs in user context (no elevation required)

### Config File Security

- Store sensitive configs on read-only network share
- Use encrypted config format (see `--encryptconfig` utility)
- Restrict access using AD security groups

### Monitoring Compliance

Use GPO reporting and SCCM/Intune to verify:
- Installation compliance
- Running process compliance
- Version compliance

## Support and Maintenance

### Centralized Logging

Configure Serilog to write to network share for centralized log collection:

Update `AppConfig.ServerUrl` to return logs to central server, or modify logging configuration to write to network share.

### Emergency Shutoff

To quickly disable the service:

1. **Remove GPO link** - Stops new launches
2. **Kill running processes via GPO**:
   - Create GPO → Computer Config → Preferences → Scheduled Tasks
   - Create immediate task: `taskkill /F /IM BrowserReporterService.exe`

### Health Monitoring

Set up monitoring for:
- Process uptime on workstations
- Last report time on server
- Error rates in logs
- Config sync failures

## Best Practices

1. **Test in pilot group** before full deployment
2. **Use staged rollout** to catch issues early
3. **Monitor logs** for the first week after deployment
4. **Document your config files** and flag choices
5. **Keep MSI versions** for rollback capability
6. **Use descriptive GPO names** with dates/versions
7. **Link GPOs at appropriate OU levels** for proper inheritance
8. **Use Security Filtering** to control which users/computers are affected
9. **Schedule deployments** outside business hours when possible
10. **Create runbook** for your helpdesk team

## Additional Resources

- WiX Toolset Documentation: https://wixtoolset.org/docs/
- Group Policy Documentation: https://docs.microsoft.com/en-us/windows-server/identity/ad-ds/
- Application Logs: `%LOCALAPPDATA%\BrowserReporter\logs.txt`
- Bootstrap Config: `%ProgramData%\BrowserReporter\bootstrap.json`
