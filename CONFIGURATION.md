# ETCStorageHelper Configuration Guide

## Quick Start

### 1. Copy Configuration Template

The library includes an `App.config` template. Copy the relevant sections to your application's `app.config` or `web.config`:

```xml
<configuration>
  <appSettings>
    <!-- Minimum required configuration -->
    <add key="ETCStorage.Commercial.TenantId" value="your-tenant-id" />
    <add key="ETCStorage.Commercial.ClientId" value="your-client-id" />
    <add key="ETCStorage.Commercial.ClientSecret" value="your-client-secret" />
    <add key="ETCStorage.Commercial.SiteUrl" value="https://tenant.sharepoint.com/sites/your-site" />
    <add key="ETCStorage.Commercial.LibraryName" value="Client Projects" />
    <add key="ETCStorage.Commercial.LogListName" value="ETC Storage Logs" />
  </appSettings>
</configuration>
```

### 2. Use in Your Application

```csharp
using ETCStorageHelper;

// Initialize with configuration
var site = SharePointSite.FromConfig(
    name: "Commercial",
    configPrefix: "ETCStorage.Commercial",
    userId: "pferrari009",
    userName: "Pablo Ferrari",
    applicationName: "My App v1.0"
);

// Use the storage
ETCFile.WriteAllText("reports/2024/report.txt", "Hello World", site);
var content = ETCFile.ReadAllText("reports/2024/report.txt", site);
```

## Configuration Keys

### Required Keys

| Key | Description | Example |
|-----|-------------|---------|
| `{Prefix}.TenantId` | Azure AD Tenant ID | `abc123-def456-...` |
| `{Prefix}.ClientId` | Azure AD Application ID | `xyz789-uvw012-...` |
| `{Prefix}.ClientSecret` | Azure AD Client Secret | `your-secret-value` |
| `{Prefix}.SiteUrl` | SharePoint site URL | `https://tenant.sharepoint.com/sites/etc` |
| `{Prefix}.LibraryName` | Document library name | `Client Projects` |

### Optional Keys

| Key | Default | Description |
|-----|---------|-------------|
| `{Prefix}.TimeoutSeconds` | `60` | HTTP timeout for operations |
| `{Prefix}.RetryAttempts` | `3` | Number of retry attempts |

### Audit Logging

The library automatically creates audit logs in a SharePoint list named **"ETC Storage Logs"** on the same site.

- **No configuration needed** - this is handled internally by the library
- **Auto-created** if the list doesn't exist
- **Auto-repaired** if columns are missing

## Multiple Environments

You can configure multiple SharePoint sites:

```xml
<appSettings>
  <!-- Production -->
  <add key="ETCStorage.Prod.TenantId" value="..." />
  <add key="ETCStorage.Prod.ClientId" value="..." />
  <add key="ETCStorage.Prod.SiteUrl" value="https://tenant.sharepoint.com/sites/prod" />
  <add key="ETCStorage.Prod.LibraryName" value="Production Files" />
  <add key="ETCStorage.Prod.LogListName" value="Prod Audit Logs" />
  
  <!-- Development -->
  <add key="ETCStorage.Dev.TenantId" value="..." />
  <add key="ETCStorage.Dev.ClientId" value="..." />
  <add key="ETCStorage.Dev.SiteUrl" value="https://tenant.sharepoint.com/sites/dev" />
  <add key="ETCStorage.Dev.LibraryName" value="Dev Files" />
  <add key="ETCStorage.Dev.LogListName" value="Dev Audit Logs" />
</appSettings>
```

Then use them:

```csharp
var prodSite = SharePointSite.FromConfig("Prod", "ETCStorage.Prod", userId, userName);
var devSite = SharePointSite.FromConfig("Dev", "ETCStorage.Dev", userId, userName);

// Operations are isolated by environment
ETCFile.WriteAllText("test.txt", "Production", prodSite);
ETCFile.WriteAllText("test.txt", "Development", devSite);
```

## Azure AD App Registration

### Required Permissions

Your Azure AD app needs these **Application** permissions:

1. **Microsoft Graph API**:
   - `Sites.ReadWrite.All` - Read/write SharePoint sites
   - `Files.ReadWrite.All` - Read/write files

2. **Grant Admin Consent** in Azure Portal

### Setup Steps

1. Go to **Azure Portal** ? **Azure Active Directory** ? **App Registrations**
2. Create a new app or select existing
3. Copy the **Application (client) ID** ? use as `ClientId`
4. Go to **API Permissions** ? Add permissions above
5. Click **Grant admin consent**
6. Go to **Certificates & secrets** ? New client secret
7. Copy the secret value ? use as `ClientSecret`
8. Note your **Tenant ID** from Azure AD overview

## SharePoint Library Setup

### Document Library

The library specified in `LibraryName` must exist in SharePoint:

1. Go to your SharePoint site
2. Verify the document library exists
3. Note the **exact name** (case-sensitive!)
4. Common names: "Client Projects", "Documents", "Shared Documents"

### Audit Log List

The SharePoint list for logging is **created automatically**:

- First time: Library creates the list with all required columns
- Existing list: Library adds any missing columns automatically
- No manual setup required!

**List Columns Created:**
- Level (Text)
- UserId (Text)
- UserName (Text)
- Operation (Text)
- SiteName (Text)
- Path (Text)
- DestinationPath (Text)
- FileSizeMB (Number)
- DurationMs (Number)
- Success (Boolean)
- ErrorMessage (Text)
- MachineName (Text)
- ApplicationName (Text)

## Security Best Practices

### 1. Protect Client Secrets

? **Don't do this:**
```xml
<!-- Committed to source control -->
<add key="ETCStorage.Prod.ClientSecret" value="actual-secret-value" />
```

? **Do this instead:**
```xml
<!-- In app.config (not committed) -->
<add key="ETCStorage.Prod.ClientSecret" value="actual-secret-value" />

<!-- In app.config.template (committed) -->
<add key="ETCStorage.Prod.ClientSecret" value="REPLACE-WITH-ACTUAL-SECRET" />
```

Or use Azure Key Vault, environment variables, or secure configuration.

### 2. Use Different Credentials per Environment

- **Production**: Dedicated production service account
- **Development**: Dedicated dev service account
- **Testing**: Dedicated test service account

### 3. Rotate Secrets Regularly

- Rotate client secrets every 90 days
- Update in Azure AD and your configuration
- Test before deploying to production

### 4. Least Privilege

- Only grant necessary permissions
- Use Application permissions (not Delegated)
- Audit access regularly

## Troubleshooting

### "Tenant ID is required"

**Problem:** Configuration not found

**Solution:** 
1. Verify config keys exist in app.config
2. Check the prefix matches: `FromConfig("Commercial", "ETCStorage.Commercial", ...)`
3. Ensure app.config is in the startup project (not the library)

### "Library 'X' not found"

**Problem:** Library name doesn't match SharePoint

**Solution:**
1. Go to SharePoint site
2. Check exact library name (case-sensitive!)
3. Update `LibraryName` in config to match exactly

### "Failed to get site ID"

**Problem:** Invalid SharePoint URL or permissions

**Solution:**
1. Verify `SiteUrl` is correct and accessible
2. Check service account has access to the site
3. Verify Azure AD app has API permissions granted

### "Access denied"

**Problem:** Service account lacks permissions

**Solution:**
1. Verify API permissions in Azure AD
2. Ensure admin consent was granted
3. Check SharePoint site permissions
4. For GCC High, use `.sharepoint.us` domain

## Example: Complete Setup

### app.config
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <add key="ETCStorage.Commercial.TenantId" value="abc-123-def" />
    <add key="ETCStorage.Commercial.ClientId" value="xyz-789-uvw" />
    <add key="ETCStorage.Commercial.ClientSecret" value="actual-secret" />
    <add key="ETCStorage.Commercial.SiteUrl" value="https://contoso.sharepoint.com/sites/etc-projects" />
    <add key="ETCStorage.Commercial.LibraryName" value="Client Projects" />
    <add key="ETCStorage.Commercial.LogListName" value="ETC Audit Logs" />
  </appSettings>
</configuration>
```

### Program.cs
```csharp
using System;
using ETCStorageHelper;

class Program
{
    static void Main()
    {
        // Initialize from config
        var site = SharePointSite.FromConfig(
            name: "Commercial",
            configPrefix: "ETCStorage.Commercial",
            userId: Environment.UserName,
            userName: Environment.UserDomainName + "\\" + Environment.UserName,
            applicationName: "My Application v1.0"
        );

        // Write a file
        Console.WriteLine("Writing file...");
        ETCFile.WriteAllText("test/hello.txt", "Hello, World!", site);

        // Read it back
        Console.WriteLine("Reading file...");
        var content = ETCFile.ReadAllText("test/hello.txt", site);
        Console.WriteLine($"Content: {content}");

        // Get the SharePoint URL
        var url = ETCFile.GetFileUrl("test/hello.txt", site);
        Console.WriteLine($"SharePoint URL: {url}");

        Console.WriteLine("Done! Check SharePoint for audit logs.");
    }
}
```

## Support

For issues or questions:
1. Check the `App.config` template in the library
2. Review this guide
3. Check Azure AD app permissions
4. Verify SharePoint site access
5. Review audit logs in SharePoint list

## Version

This configuration format is compatible with **ETCStorageHelper v1.1.0+**

Changes from v1.0:
- Added `LogListName` configuration (optional, defaults to "ETC Storage Logs")
- Auto-creates SharePoint audit list if it doesn't exist
- Auto-adds missing columns to existing lists
