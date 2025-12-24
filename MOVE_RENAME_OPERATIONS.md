# Move and Rename Operations Guide

**New in v1.3.0 & v1.4.0** | Complete File and Folder Management

---

## 🎉 What's New

ETCStorageHelper now provides **complete file and folder management** with the addition of Move and Rename operations. You can now:

✅ Rename files and folders in place  
✅ Move files and folders to different locations  
✅ Move content between SharePoint sites  
✅ Control overwrite behavior (just like System.IO)  
✅ Execute operations synchronously or asynchronously

---

## 🚀 Quick Examples

### Rename a File
```csharp
// Change "draft-report.pdf" to "final-report.pdf"
ETCFile.Move(
    "ClientA/Job001/draft-report.pdf",
    "ClientA/Job001/final-report.pdf",
    site
);
```

### Move a File to Different Folder
```csharp
// Move from Drafts to Finals folder
ETCFile.Move(
    "ClientA/Job001/Drafts/report.pdf",
    "ClientA/Job001/Finals/report.pdf",
    site
);
```

### Rename a Folder
```csharp
// Rename project folder
ETCDirectory.Move(
    "ClientA/2025/Job12345-Draft",
    "ClientA/2025/Job12345-Final",
    site
);
```

### Move Entire Project Folder
```csharp
// Move project to different client (all nested files move too!)
ETCDirectory.Move(
    "ClientA/Archive/OldProject",
    "Archive/2024/ClientA/OldProject",
    site
);
```

---

## 📋 File Operations

### ETCFile.Move()

#### Basic Syntax
```csharp
ETCFile.Move(string sourceFileName, string destFileName, SharePointSite site, bool overwrite = false)
```

#### Example 1: Rename File in Place
```csharp
var site = SharePointSite.FromConfig("Commercial", "ETCStorage.Commercial");

// Rename without changing location
ETCFile.Move(
    "Environmental/ClientA/2025/Job001/analysis-v1.xlsx",
    "Environmental/ClientA/2025/Job001/analysis-v2.xlsx",
    site
);
```

#### Example 2: Move to Different Folder
```csharp
// Move file from one job to another
ETCFile.Move(
    "Environmental/ClientA/2025/Job001/shared-data.xlsx",
    "Environmental/ClientA/2025/Job002/shared-data.xlsx",
    site
);
```

#### Example 3: Move with Overwrite
```csharp
// Replace existing file at destination
ETCFile.Move(
    "ClientA/Temp/report.pdf",
    "ClientA/Final/report.pdf",  // Overwrites if exists
    site,
    overwrite: true
);
```

#### Example 4: Move Between Sites
```csharp
var commercialSite = SharePointSite.FromConfig("Commercial", "ETCStorage.Commercial");
var gccHighSite = SharePointSite.FromConfig("GCCHigh", "ETCStorage.GCCHigh");

// Move CUI data to GCC High environment
ETCFile.Move(
    "ClientA/Job001/cui-report.pdf", commercialSite,
    "ClientA/Job001/cui-report.pdf", gccHighSite
);
```

---

## 📁 Folder Operations

### ETCDirectory.Move()

#### Basic Syntax
```csharp
ETCDirectory.Move(string sourcePath, string destinationPath, SharePointSite site, bool overwrite = false)
```

#### Example 1: Rename Folder
```csharp
// Rename project folder
ETCDirectory.Move(
    "Environmental/ClientA/2025/Job12345-InProgress",
    "Environmental/ClientA/2025/Job12345-Completed",
    site
);
```

#### Example 2: Move to Different Parent
```csharp
// Reorganize: Move job folder to Archive
ETCDirectory.Move(
    "Environmental/ClientA/Active/2024/Job001",
    "Environmental/ClientA/Archive/2024/Job001",
    site
);
```

#### Example 3: Restructure Project Hierarchy
```csharp
// Change from: Category/Client/Job
//          to: Client/Category/Job

ETCDirectory.Move(
    "Environmental/ACME Corp/Job001",
    "ACME Corp/Environmental/Job001",
    site
);
```

#### Example 4: Archive Completed Projects
```csharp
// Move entire year to archive
ETCDirectory.Move(
    "ClientA/Active/2023",
    "ClientA/Archive/2023",
    site
);

// All nested folders and files are preserved!
```

---

## 🔄 Async Operations

For large files or folders, use async versions that return immediately and complete in the background.

### ETCFileAsync.MoveAsync()

```csharp
// Move large file asynchronously
var handle = ETCFileAsync.MoveAsync(
    "ClientA/Data/large-dataset-100mb.zip",
    "ClientA/Archive/large-dataset-100mb.zip",
    site,
    overwrite: false,
    onSuccess: path => Console.WriteLine($"✓ File moved: {path}"),
    onError: (path, ex) => Console.WriteLine($"✗ Move failed: {ex.Message}")
);

Console.WriteLine($"Move queued with ID: {handle.UploadId}");
// Your app continues immediately, move happens in background
```

### ETCDirectoryAsync.MoveAsync()

```csharp
// Move large project folder with hundreds of files
var handle = ETCDirectoryAsync.MoveAsync(
    "ClientA/Active/LargeProject",
    "ClientA/Archive/LargeProject",
    site,
    overwrite: false,
    onSuccess: path => 
    {
        Console.WriteLine($"✓ Project archived: {path}");
        SendNotificationEmail("Project archived successfully");
    },
    onError: (path, ex) => 
    {
        Console.WriteLine($"✗ Archive failed: {ex.Message}");
        LogError(ex);
    }
);

// Monitor progress
Console.WriteLine("Archiving project in background...");
```

---

## ⚙️ Overwrite Behavior (v1.4.0)

Control what happens when the destination already exists.

### Default: Fail on Conflict (Safe)
```csharp
// Throws error if destination exists
ETCFile.Move(source, destination, site);
// or explicitly
ETCFile.Move(source, destination, site, overwrite: false);

// Error: "nameAlreadyExists" - Prevents accidental data loss
```

### Overwrite: Replace if Exists
```csharp
// Replaces destination if it exists
ETCFile.Move(source, destination, site, overwrite: true);

// Mimics System.IO.File.Move(source, dest, overwrite: true) from .NET Core 3.0+
```

### Comparison with System.IO

| Scenario | System.IO (.NET Framework) | System.IO (.NET Core 3.0+) | ETCFile |
|----------|---------------------------|---------------------------|---------|
| Dest doesn't exist | ✅ Success | ✅ Success | ✅ Success |
| Dest exists, overwrite: false | ❌ IOException | ❌ IOException | ❌ Exception |
| Dest exists, overwrite: true | ❌ Not supported | ✅ Success | ✅ Success |

**ETCStorageHelper provides .NET Core 3.0+ functionality on .NET Framework 4.6!**

---

## 🎯 Real-World Scenarios

### Scenario 1: Promote Draft to Final
```csharp
public void PromoteReportToFinal(string clientName, int jobNumber)
{
    var draftPath = ETCPath.Combine(
        clientName, "Jobs", $"Job{jobNumber}", "Drafts", "report.pdf");
    
    var finalPath = ETCPath.Combine(
        clientName, "Jobs", $"Job{jobNumber}", "Final", "report.pdf");
    
    // Move draft to final location
    ETCFile.Move(draftPath, finalPath, _site, overwrite: true);
    
    Console.WriteLine("Report promoted to final!");
}
```

### Scenario 2: Archive Completed Project
```csharp
public void ArchiveCompletedProject(string clientName, int year, int jobNumber)
{
    var activePath = ETCPath.Combine(
        clientName, "Active", year.ToString(), $"Job{jobNumber}");
    
    var archivePath = ETCPath.Combine(
        clientName, "Archive", year.ToString(), $"Job{jobNumber}");
    
    // Move entire project folder
    ETCDirectory.Move(activePath, archivePath, _site);
    
    Console.WriteLine($"Project archived: Job{jobNumber}");
}
```

### Scenario 3: Rename Based on Status Change
```csharp
public void UpdateProjectStatus(string projectPath, string newStatus)
{
    // From: "ClientA/Projects/Job001-InReview"
    // To:   "ClientA/Projects/Job001-Approved"
    
    var directory = ETCPath.GetDirectoryName(projectPath);
    var currentName = ETCPath.GetFileName(projectPath);
    
    // Extract base name (before status)
    var baseName = currentName.Split('-')[0];  // "Job001"
    var newName = $"{baseName}-{newStatus}";
    
    var newPath = ETCPath.Combine(directory, newName);
    
    ETCDirectory.Move(projectPath, newPath, _site);
    
    Console.WriteLine($"Project renamed to: {newName}");
}
```

### Scenario 4: Migrate to GCC High
```csharp
public async Task MigrateCUIProject(string clientName, int jobNumber)
{
    var projectPath = ETCPath.Combine(clientName, $"Job{jobNumber}");
    
    // Move from Commercial to GCC High (async for large projects)
    var handle = ETCDirectoryAsync.MoveAsync(
        projectPath, _commercialSite,
        projectPath, _gccHighSite,
        overwrite: false,
        onSuccess: path => 
        {
            Console.WriteLine($"✓ Project migrated to GCC High: {path}");
            SendComplianceNotification(clientName, jobNumber);
        },
        onError: (path, ex) => 
        {
            Console.WriteLine($"✗ Migration failed: {ex.Message}");
            RollbackMigration(clientName, jobNumber);
        }
    );
    
    Console.WriteLine("Migration started in background...");
}
```

### Scenario 5: Batch Rename Files
```csharp
public void ApplyNamingConvention(string folderPath, SharePointSite site)
{
    // Get all files in folder
    var files = ETCDirectory.GetFilesWithInfo(folderPath, site);
    
    foreach (var file in files.Where(f => !f.IsFolder))
    {
        var oldPath = file.FullPath;
        var fileName = ETCPath.GetFileName(oldPath);
        
        // Apply naming convention: Add date prefix
        var newFileName = $"{DateTime.Now:yyyyMMdd}_{fileName}";
        var newPath = ETCPath.Combine(
            ETCPath.GetDirectoryName(oldPath), 
            newFileName
        );
        
        ETCFile.Move(oldPath, newPath, site);
        Console.WriteLine($"Renamed: {fileName} → {newFileName}");
    }
}
```

---

## ✅ Important Notes

### Move vs Copy
| Operation | Source After Operation | Use Case |
|-----------|----------------------|----------|
| **Copy** | Still exists | Create duplicate, backup |
| **Move** | Deleted | Rename, relocate, reorganize |

### Folder Moves
✅ All nested files and subfolders move together  
✅ Folder hierarchy is preserved  
✅ Move is atomic (uses SharePoint PATCH API)  
✅ Fast - doesn't copy then delete

### Cross-Site Moves
⚠️ Between different SharePoint sites, the operation:
1. Copies file to destination site
2. Deletes file from source site
3. Takes longer than same-site moves

### Performance
- **Same-site moves:** Very fast (~500ms) - just metadata update
- **Cross-site moves:** Slower - actual data transfer required
- **Large folders:** Use async versions to avoid blocking
- **Overwrite:** No performance difference

---

## 🔄 Migration from Copy/Delete Pattern

### Before (v1.2.0 and earlier)
```csharp
// Old way: Copy then manually delete
ETCFile.Copy(sourcePath, destPath, site);
ETCFile.Delete(sourcePath, site);

// Problems:
// - Two operations instead of one
// - If copy succeeds but delete fails, you have duplicates
// - No atomic operation
```

### After (v1.3.0+)
```csharp
// New way: Single atomic move operation
ETCFile.Move(sourcePath, destPath, site);

// Benefits:
// - Single operation
// - Atomic (all or nothing)
// - Faster
// - Cleaner code
```

---

## 📊 Feature Availability

| Feature | v1.3.0 | v1.4.0 |
|---------|--------|--------|
| ETCFile.Move() | ✅ | ✅ |
| ETCDirectory.Move() | ✅ | ✅ |
| ETCFileAsync.MoveAsync() | ✅ | ✅ |
| ETCDirectoryAsync.MoveAsync() | ✅ | ✅ |
| Cross-site moves | ✅ | ✅ |
| **overwrite parameter** | ❌ | ✅ |
| Callbacks (async) | ✅ | ✅ |

---

## 🎓 Best Practices

### 1. Check Before Overwriting
```csharp
// Good: Explicit decision
if (ETCFile.Exists(destPath, site))
{
    var userConfirmed = AskUserForConfirmation(
        $"File '{destPath}' already exists. Overwrite?");
    
    if (userConfirmed)
    {
        ETCFile.Move(source, destPath, site, overwrite: true);
    }
}
else
{
    ETCFile.Move(source, destPath, site);
}
```

### 2. Use Async for Large Operations
```csharp
// Good: Non-blocking UI
var handle = ETCDirectoryAsync.MoveAsync(
    largeFolderPath, newPath, site,
    onSuccess: path => UpdateUI("Move complete"),
    onError: (path, ex) => ShowError(ex.Message)
);

ShowProgressIndicator("Moving folder...");
```

### 3. Handle Errors Gracefully
```csharp
// Good: Comprehensive error handling
try
{
    ETCFile.Move(sourcePath, destPath, site);
}
catch (FileNotFoundException ex)
{
    Console.WriteLine("Source file not found - may have been already moved");
}
catch (DirectoryNotFoundException ex)
{
    Console.WriteLine("Destination folder doesn't exist - creating it...");
    ETCDirectory.CreateDirectory(ETCPath.GetDirectoryName(destPath), site);
    ETCFile.Move(sourcePath, destPath, site);
}
catch (Exception ex) when (ex.Message.Contains("nameAlreadyExists"))
{
    Console.WriteLine("Destination already exists - use overwrite: true");
}
```

### 4. Validate Paths
```csharp
// Good: Validation before moving
public void SafeMove(string source, string dest, SharePointSite site)
{
    if (string.IsNullOrWhiteSpace(source))
        throw new ArgumentException("Source path cannot be empty");
    
    if (string.IsNullOrWhiteSpace(dest))
        throw new ArgumentException("Destination path cannot be empty");
    
    if (source.Equals(dest, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Source and destination cannot be the same");
    
    if (!ETCFile.Exists(source, site))
        throw new FileNotFoundException($"Source file not found: {source}");
    
    ETCFile.Move(source, dest, site);
}
```

---

## 📚 API Reference

### ETCFile Methods

```csharp
// Move within same site
void Move(string sourceFileName, string destFileName, SharePointSite site, bool overwrite = false)

// Move between sites
void Move(string sourceFileName, SharePointSite sourceSite, 
          string destFileName, SharePointSite destSite, bool overwrite = false)
```

### ETCDirectory Methods

```csharp
// Move folder within same site
void Move(string sourcePath, string destinationPath, SharePointSite site, bool overwrite = false)
```

### ETCFileAsync Methods

```csharp
// Async move within same site
UploadHandle MoveAsync(string sourceFileName, string destFileName, SharePointSite site, 
                       bool overwrite = false, 
                       Action<string> onSuccess = null, 
                       Action<string, Exception> onError = null)

// Async move between sites
UploadHandle MoveAsync(string sourceFileName, SharePointSite sourceSite,
                       string destFileName, SharePointSite destSite,
                       bool overwrite = false,
                       Action<string> onSuccess = null,
                       Action<string, Exception> onError = null)
```

### ETCDirectoryAsync Methods

```csharp
// Async move folder
UploadHandle MoveAsync(string sourcePath, string destinationPath, SharePointSite site,
                       bool overwrite = false,
                       Action<string> onSuccess = null,
                       Action<string, Exception> onError = null)
```

---

## 🎉 Summary

Move and Rename operations complete the ETCStorageHelper API, giving you **full control over your SharePoint files and folders**:

✅ **Rename** - Change file/folder names in place  
✅ **Relocate** - Move to different folders  
✅ **Reorganize** - Restructure your entire hierarchy  
✅ **Migrate** - Move content between SharePoint sites  
✅ **Control** - Decide when to overwrite existing content  
✅ **Async** - Non-blocking operations for large files/folders

**The API you know, now with complete file management!**

---

## 📞 Learn More

- **Main Documentation:** [README.md](README.md)
- **Version History:** See README.md Version History section
- **API Reference:** See README.md API Reference section

---

*Added in v1.3.0 (Move operations) and v1.4.0 (Overwrite support)*

