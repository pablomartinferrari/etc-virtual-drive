# How to Use ETCStorageHelper v1.0.4 in Your Test Project

## Option 1: Direct DLL Reference (Simplest)

### 1. Copy the DLL
Copy these files from the library's `bin\Release` folder to your test project:
```
ETCStorageHelper\bin\Release\ETCStorageHelper.dll
ETCStorageHelper\bin\Release\ETCStorageHelper.xml (for IntelliSense)
```

### 2. Add Reference in Visual Studio
1. Right-click on your test project ? **Add** ? **Reference**
2. Click **Browse** button
3. Navigate to where you copied `ETCStorageHelper.dll`
4. Select it and click **Add**
5. Click **OK**

### 3. Add NuGet Dependency
Your test project needs the same dependency:
```
Install-Package Newtonsoft.Json -Version 13.0.3
```

### 4. Add App.config
Copy the example configuration to your test project's `app.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <!-- Replace with your actual values -->
    <add key="ETCStorage.Commercial.TenantId" value="your-tenant-id-guid" />
    <add key="ETCStorage.Commercial.ClientId" value="your-client-id-guid" />
    <add key="ETCStorage.Commercial.ClientSecret" value="your-client-secret" />
    <add key="ETCStorage.Commercial.SiteUrl" value="https://your-tenant.sharepoint.com/sites/your-site" />
    <add key="ETCStorage.Commercial.LibraryName" value="Documents" />
  </appSettings>
</configuration>
```

### 5. Write Test Code
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
            applicationName: "Test App v1.0"
        );

        // Write a file
        Console.WriteLine("Writing file to SharePoint...");
        ETCFile.WriteAllText("test/hello.txt", "Hello from v1.0.4!", site);
        Console.WriteLine("? File written successfully");

        // Read it back
        Console.WriteLine("Reading file from SharePoint...");
        var content = ETCFile.ReadAllText("test/hello.txt", site);
        Console.WriteLine($"? File content: {content}");

        // Get SharePoint URL
        var url = ETCFile.GetFileUrl("test/hello.txt", site);
        Console.WriteLine($"? SharePoint URL: {url}");

        Console.WriteLine("\nCheck SharePoint for the 'ETC Storage Logs' list to see audit trail!");
        Console.ReadKey();
    }
}
```

---

## Option 2: NuGet Package (For Distribution)

### 1. Create NuGet Package
From the library's directory:
```powershell
nuget pack ETCStorageHelper\ETCStorageHelper.csproj -Properties Configuration=Release
```

This creates: `ETCStorageHelper.1.0.4.nupkg`

### 2. Install in Test Project
```powershell
# If you have a local NuGet feed
Install-Package ETCStorageHelper -Version 1.0.4 -Source C:\path\to\packages

# Or install from the .nupkg file directly
Install-Package C:\path\to\ETCStorageHelper.1.0.4.nupkg
```

---

## Option 3: Project Reference (For Development)

### 1. Add Project Reference
In Visual Studio:
1. Right-click your test project ? **Add** ? **Reference**
2. Select **Projects** tab
3. Check **ETCStorageHelper**
4. Click **OK**

**Benefit:** Changes to the library automatically reflect in your test project

---

## Files Built (v1.0.4)

Located in `ETCStorageHelper\bin\Release\`:

| File | Size | Description |
|------|------|-------------|
| `ETCStorageHelper.dll` | ~100 KB | Main assembly |
| `ETCStorageHelper.xml` | ~44 KB | IntelliSense documentation |
| `ETCStorageHelper.pdb` | ~253 KB | Debug symbols (optional) |
| `ETCStorageHelper.dll.config` | ~7 KB | Library configuration |

---

## What's New in v1.0.4

### Bug Fixes
- ? **Auto-Repair SharePoint Lists**: Automatically detects and adds missing columns to manually created audit lists
- ? **Large File Upload Fix**: Dynamic timeout calculation prevents failures on 60MB+ files
- ? **Better Error Messages**: Clear guidance when configuration or lists have issues

### Changes
- Audit log list name is now hardcoded to "ETC Storage Logs" (not consumer-configurable)
- Timeout scales automatically: `Max(180 seconds, file size in MB + 120 seconds)`

### See Also
- `BUGFIXES.md` - Detailed bug fix documentation
- `CONFIGURATION.md` - Complete configuration guide
- `App.config` - Example configuration template

---

## Verify Installation

Run this test to verify everything works:

```csharp
using System;
using ETCStorageHelper;

// This should compile without errors
var site = new SharePointSite(
    "Test",
    "tenant-id",
    "client-id", 
    "secret",
    "https://tenant.sharepoint.com/sites/test",
    "Documents"
);

Console.WriteLine($"ETCStorageHelper v1.0.4 loaded successfully!");
Console.WriteLine($"Site: {site.Name}");
```

If it compiles and runs, you're all set! ??

---

## Troubleshooting

### "Could not load file or assembly 'Newtonsoft.Json'"
**Solution:** Install Newtonsoft.Json in your test project:
```
Install-Package Newtonsoft.Json -Version 13.0.3
```

### "ETCStorage.Commercial.TenantId not found"
**Solution:** Ensure your `app.config` has the configuration keys and matches the prefix you use in `FromConfig()`

### "Access Denied" or "401 Unauthorized"
**Solution:** 
1. Verify Azure AD app has correct permissions (Sites.ReadWrite.All, Files.ReadWrite.All)
2. Grant admin consent in Azure Portal
3. Check service account has access to SharePoint site

---

## Build Information

- **Version:** 1.0.4
- **Build Date:** December 12, 2025
- **Target Framework:** .NET Framework 4.6
- **Dependencies:** Newtonsoft.Json 13.0.3
- **Build Configuration:** Release
- **Assembly Version:** 1.0.4.0
