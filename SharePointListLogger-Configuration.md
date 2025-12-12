# SharePoint List Logger - Configuration Guide

## Overview

The `SharePointListLogger` has been enhanced with the following features:
1. **Configurable list name** - No longer hardcoded to "ETC Storage Logs"
2. **Auto-repair missing columns** - Automatically adds missing columns to existing lists
3. **Flexible schema** - Works with manually created lists by adding required fields

## Configuration

### App.config / Web.config Setup

Add the `LogListName` configuration key to specify which SharePoint list to use for logging:

```xml
<appSettings>
  <!-- Existing settings -->
  <add key="ETCStorage.Commercial.TenantId" value="your-tenant-id" />
  <add key="ETCStorage.Commercial.ClientId" value="your-client-id" />
  <add key="ETCStorage.Commercial.ClientSecret" value="your-client-secret" />
  <add key="ETCStorage.Commercial.SiteUrl" value="https://tenant.sharepoint.com/sites/etc-projects" />
  <add key="ETCStorage.Commercial.LibraryName" value="Client Projects" />
  
  <!-- NEW: Specify the SharePoint list name for logging -->
  <add key="ETCStorage.Commercial.LogListName" value="My Custom Log List" />
</appSettings>
```

**Default Value:** If not specified, defaults to `"ETC Storage Logs"`

### Multiple Environments Example

```xml
<appSettings>
  <!-- Commercial Environment -->
  <add key="ETCStorage.Commercial.LogListName" value="ETC Prod Logs" />
  
  <!-- Development Environment -->
  <add key="ETCStorage.Dev.LogListName" value="ETC Dev Logs" />
  
  <!-- GCC High Environment -->
  <add key="ETCStorage.GCCHigh.LogListName" value="ETC Secure Logs" />
</appSettings>
```

## Usage

### Using FromConfig (Recommended)

The logger automatically uses the configured list name:

```csharp
var site = SharePointSite.FromConfig(
    name: "Commercial",
    configPrefix: "ETCStorage.Commercial",
    userId: "pferrari009",
    userName: "Pablo Ferrari",
    applicationName: "My App v1.0"
);

// Logger is automatically configured to use the list name from app.config
// ETCStorage.Commercial.LogListName = "My Custom Log List"
```

### Manual Creation

You can also manually specify a different list name:

```csharp
var logger = new SharePointListLogger(
    siteUrl: "https://tenant.sharepoint.com/sites/etc-projects",
    listName: "Special Audit Log",  // Custom list name
    tenantId: "...",
    clientId: "...",
    clientSecret: "..."
);
```

Or using FromSite:

```csharp
var site = new SharePointSite(...);
var logger = SharePointListLogger.FromSite(site, "My Custom List Name");
site.Logger = logger;
```

## Auto-Repair Feature

### How It Works

When the logger connects to SharePoint:

1. **List Exists**: Checks if the configured list exists
2. **Column Verification**: Verifies all required columns are present
3. **Auto-Add Missing Columns**: Automatically adds any missing columns
4. **Continue Logging**: Proceeds with logging operations

### Required Columns

The logger requires these columns (automatically created if missing):

| Column Name | Type | Description |
|------------|------|-------------|
| **Level** | Text | Log level (Info, Warning, Error) |
| **UserId** | Text | User identifier |
| **UserName** | Text | User display name |
| **Operation** | Text | Operation type (WriteFile, ReadFile, etc.) |
| **SiteName** | Text | SharePoint site name |
| **Path** | Text | File/folder path |
| **DestinationPath** | Text | Destination path (for copy operations) |
| **FileSizeMB** | Number | File size in megabytes |
| **DurationMs** | Number | Operation duration in milliseconds |
| **Success** | Boolean | Whether operation succeeded |
| **ErrorMessage** | Text | Error message (if failed) |
| **MachineName** | Text | Machine name where operation occurred |
| **ApplicationName** | Text | Application name |

### Console Output Examples

**When all columns exist:**
```
[SharePointListLogger] Found existing list 'My Custom Log List' with ID: abc-123-def
[SharePointListLogger] ? All required columns exist in list 'My Custom Log List'
```

**When columns are missing:**
```
[SharePointListLogger] Found existing list 'My Custom Log List' with ID: abc-123-def
[SharePointListLogger] Found 3 missing column(s) in list 'My Custom Log List', adding them...
[SharePointListLogger] ? Added column 'Level' to list 'My Custom Log List'
[SharePointListLogger] ? Added column 'FileSizeMB' to list 'My Custom Log List'
[SharePointListLogger] ? Added column 'DurationMs' to list 'My Custom Log List'
[SharePointListLogger] ? Column verification complete for list 'My Custom Log List'
```

**When list doesn't exist:**
```
[SharePointListLogger] Creating SharePoint list 'My Custom Log List'...
[SharePointListLogger] ? SharePoint list 'My Custom Log List' created successfully!
[SharePointListLogger] Access at: https://tenant.sharepoint.com/sites/etc-projects/Lists/My%20Custom%20Log%20List
```

## Scenarios

### Scenario 1: Fresh Setup
- List doesn't exist
- Logger creates it with all required columns
- Ready to log immediately

### Scenario 2: Manually Created List
- You created "Audit Logs" list manually in SharePoint
- List has only: Title, Path, UserName
- Logger automatically adds missing columns:
  - Level
  - UserId
  - Operation
  - SiteName
  - DestinationPath
  - FileSizeMB
  - DurationMs
  - Success
  - ErrorMessage
  - MachineName
  - ApplicationName
- Starts logging with complete schema

### Scenario 3: Multiple Applications Sharing a List
- App A uses "Shared Audit Log"
- App B also uses "Shared Audit Log"
- Both apps contribute to the same centralized log
- Schema is maintained automatically

### Scenario 4: Different Lists per Environment
```csharp
// Production
var prodSite = SharePointSite.FromConfig(
    "Production", 
    "ETCStorage.Prod",  // LogListName = "Production Logs"
    userId, userName
);

// Staging
var stageSite = SharePointSite.FromConfig(
    "Staging", 
    "ETCStorage.Stage",  // LogListName = "Staging Logs"
    userId, userName
);

// Development
var devSite = SharePointSite.FromConfig(
    "Development", 
    "ETCStorage.Dev",  // LogListName = "Dev Logs"
    userId, userName
);
```

## Benefits

? **No manual SharePoint list setup** - Logger creates/configures automatically  
? **Works with existing lists** - Adds missing columns without breaking existing data  
? **Environment-specific logs** - Use different lists for prod/dev/test  
? **Multi-app support** - Multiple apps can share one list  
? **Zero downtime** - Columns added without interrupting logging  
? **Fail-safe** - If column creation fails, logs to local file as backup  

## Migration from Hardcoded "ETC Storage Logs"

### Before (Hardcoded)
```csharp
// Always used "ETC Storage Logs" - no choice
var logger = SharePointListLogger.FromSite(site);
```

### After (Configurable)
```csharp
// Option 1: Use default "ETC Storage Logs" (no change needed)
var logger = SharePointListLogger.FromSite(site);

// Option 2: Specify in app.config
<add key="ETCStorage.Commercial.LogListName" value="My Custom Logs" />

// Option 3: Specify at runtime
var logger = SharePointListLogger.FromSite(site, "My Custom Logs");
```

## Troubleshooting

### Issue: "Field 'Level' is not recognized"

**Cause:** Existing list doesn't have the Level column

**Solution:** The logger now automatically adds it! Just run your application and it will:
1. Detect the missing column
2. Add it to the list
3. Continue logging

**Console Output:**
```
[SharePointListLogger] Found 1 missing column(s) in list 'My List', adding them...
[SharePointListLogger] ? Added column 'Level' to list 'My List'
```

### Issue: Custom list with different columns

**Scenario:** You created a list with custom columns for other purposes

**Solution:** 
- Logger adds its required columns alongside your custom ones
- Your custom columns are preserved
- Logger only writes to its own columns
- You can have both your data and audit logs in the same list

### Issue: Permissions error adding columns

**Error:** `Failed to add column 'Level': Access denied`

**Solution:** Ensure the service account has these SharePoint permissions:
- **List permissions:** Edit Items, Add Items
- **Site permissions:** Design (to add columns)

Or manually add the missing columns using the column definitions above.

## Best Practices

1. **Use configuration** - Specify list names in app.config for easy environment changes
2. **Separate environments** - Use different lists for prod/dev/test
3. **Share lists wisely** - Multiple related apps can share a list for unified audit trail
4. **Monitor auto-repair** - Check console output to see when columns are added
5. **Grant permissions** - Ensure service account can add columns (or manually pre-create them)

## Example: Complete Setup

### App.config
```xml
<appSettings>
  <!-- SharePoint Configuration -->
  <add key="ETCStorage.Commercial.TenantId" value="abc-123-def" />
  <add key="ETCStorage.Commercial.ClientId" value="xyz-789-uvw" />
  <add key="ETCStorage.Commercial.ClientSecret" value="secret" />
  <add key="ETCStorage.Commercial.SiteUrl" value="https://contoso.sharepoint.com/sites/etc" />
  <add key="ETCStorage.Commercial.LibraryName" value="Client Projects" />
  <add key="ETCStorage.Commercial.LogListName" value="Environmental Audit Logs" />
</appSettings>
```

### Code
```csharp
// Initialize with configured list name
var site = SharePointSite.FromConfig(
    "Commercial",
    "ETCStorage.Commercial",
    userId: "pferrari009",
    userName: "Pablo Ferrari",
    applicationName: "ETC Desktop v2.1"
);

// Use the storage - logging happens automatically to "Environmental Audit Logs"
ETCFile.WriteAllText("reports/2024/annual.pdf", fileData, site);
```

### Result in SharePoint
- List: **Environmental Audit Logs**
- Columns: All 13 required columns (auto-created if missing)
- Log Entry:
  - **Title:** "WriteFile by Pablo Ferrari"
  - **Level:** "Info"
  - **Operation:** "WriteFile"
  - **Path:** "reports/2024/annual.pdf"
  - **UserName:** "Pablo Ferrari"
  - **ApplicationName:** "ETC Desktop v2.1"
  - **Success:** Yes
  - **DurationMs:** 1523
  - etc.
