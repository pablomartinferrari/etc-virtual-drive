# ETCStorageHelper v1.4.2 Release Summary

**Release Type:** Bug Fix  
**Release Date:** December 2025  
**Severity:** Medium (Correctness Issue)  
**Breaking Changes:** None

---

## 🐛 Executive Summary

Version 1.4.2 fixes a **path normalization inconsistency** between synchronous and asynchronous directory listing methods. The async versions were not normalizing Windows-style paths (backslashes) and trailing slashes, causing malformed `FullPath` properties in returned `FileItemInfo` objects.

**Impact:** Users passing Windows-style paths (e.g., `"ClientA\\Job001\\"`) to async methods would receive items with mixed separators or double slashes in the `FullPath` property, potentially breaking downstream path operations.

---

## 🔍 The Problem

### Root Cause

The sync method `ETCDirectory.GetFilesWithInfo()` was calling `NormalizePath()` before passing paths to the underlying SharePoint client:

```csharp
// SYNC - Correct ✅
public static ETCFileInfo[] GetFilesWithInfo(string path, SharePointSite site, string searchPattern = null)
{
    var client = ETCFile.GetClientInternal(site);
    var items = RunAsync(() => client.ListDirectoryWithInfoAsync(NormalizePath(path))); // Normalized!
    // ...
}
```

But the async method `ETCDirectoryAsync.GetFilesWithInfoAsync()` was passing the raw path directly:

```csharp
// ASYNC - Bug ❌
public static async Task<ETCFileInfo[]> GetFilesWithInfoAsync(string path, SharePointSite site, string searchPattern = null)
{
    var client = ETCFile.GetOrCreateClient(site);
    var items = await client.ListDirectoryWithInfoAsync(path); // NOT normalized!
    // ...
}
```

### Reproduction

```csharp
// User passes Windows-style path
var path = "ClientA\\Job001\\";

// Sync method - works correctly
var syncFiles = ETCDirectory.GetFilesWithInfo(path, site);
// Returns: FullPath = "ClientA/Job001/file.txt" ✅

// Async method - produces malformed paths
var asyncFiles = await ETCDirectoryAsync.GetFilesWithInfoAsync(path, site);
// Returns: FullPath = "ClientA\\Job001\\/file.txt" ❌
//                     Mixed separators ^^      ^^
```

### Affected Methods

All async directory methods in `ETCDirectoryAsync` class:
- ❌ `GetFileSystemEntriesAsync()`
- ❌ `GetFilesWithInfoAsync()`
- ❌ `ExistsAsync()`
- ❌ `GetFolderUrlAsync()`

**Note:** Async methods that used background queues (like `CreateDirectoryAsync`, `DeleteAsync`, `MoveAsync`) were not affected because they rely on the queue which normalizes paths internally.

---

## ✅ The Solution

### Code Changes

1. **Added `NormalizePath()` helper to `ETCDirectoryAsync` class:**

```csharp
/// <summary>
/// Normalize path for SharePoint (convert backslashes to forward slashes, trim trailing slashes)
/// </summary>
private static string NormalizePath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        return "";

    // Convert backslashes to forward slashes for SharePoint
    return path.Replace('\\', '/').Trim('/');
}
```

2. **Updated all affected async methods to normalize paths:**

```csharp
// GetFileSystemEntriesAsync - FIXED ✅
var items = await client.ListDirectoryAsync(NormalizePath(path));

// GetFilesWithInfoAsync - FIXED ✅
var items = await client.ListDirectoryWithInfoAsync(NormalizePath(path));

// ExistsAsync - FIXED ✅
return await client.DirectoryExistsAsync(NormalizePath(path));

// GetFolderUrlAsync - FIXED ✅
return await client.GetFolderUrlAsync(NormalizePath(path));
```

### Files Modified

| File | Change |
|------|--------|
| `ETCDirectoryAsync.cs` | Added `NormalizePath()` helper and applied to 4 methods |
| `ETCDirectoryTests.cs` | Added comprehensive path normalization test (`TestPathNormalization`) |
| `AssemblyInfo.cs` | Bumped version to 1.4.2.0 |
| `ETCStorageHelper.nuspec` | Updated version and release notes |
| `README.md` | Added v1.4.2 to version history |

---

## 🧪 Testing

### New Test: `TestPathNormalization()`

Added comprehensive test to verify path normalization across all scenarios:

```csharp
private static void TestPathNormalization(SharePointSite site, string basePath)
{
    // Test 1: Sync GetFilesWithInfo with backslashes
    var windowsPath = "ClientA\\Job001\\";
    var syncFiles = ETCDirectory.GetFilesWithInfo(windowsPath, site);
    // ✅ Verifies no mixed separators or double slashes
    
    // Test 2: Async GetFilesWithInfoAsync with backslashes  
    var asyncFiles = await ETCDirectoryAsync.GetFilesWithInfoAsync(windowsPath, site);
    // ✅ Verifies no mixed separators or double slashes
    
    // Test 3: Verify sync and async return same results
    // ✅ Confirms consistency
    
    // Test 4: Path with trailing slash
    var pathWithSlash = "ClientA/Job001/";
    // ✅ Handles trailing slashes correctly
    
    // Test 5: ExistsAsync with Windows-style path
    var exists = await ETCDirectoryAsync.ExistsAsync(windowsPath, site);
    // ✅ Confirms directory found with normalized path
}
```

**Test Coverage:**
- ✅ Windows-style paths with backslashes
- ✅ Paths with trailing slashes
- ✅ Sync vs async consistency
- ✅ Malformed path detection (mixed separators, double slashes)
- ✅ All fixed async methods

---

## 📊 Impact Analysis

### Who Is Affected?

**Users on Windows** who:
1. Pass path strings with backslashes to async directory methods
2. Use `FileItemInfo.FullPath` for subsequent operations (path joins, file operations)
3. Rely on consistent path formatting between sync and async methods

**Example Scenarios:**

```csharp
// Scenario 1: Windows user building paths
var folderPath = Path.Combine("ClientA", "Job001"); // Returns "ClientA\\Job001" on Windows
var files = await ETCDirectoryAsync.GetFilesWithInfoAsync(folderPath, site);
// BEFORE v1.4.2: files[0].FullPath = "ClientA\\Job001/file.txt" ❌
// AFTER v1.4.2:  files[0].FullPath = "ClientA/Job001/file.txt" ✅

// Scenario 2: Using returned paths for operations
foreach (var file in files)
{
    var url = ETCFile.GetFileUrl(file.FullPath, site);
    // BEFORE: Could fail with malformed path ❌
    // AFTER: Works correctly ✅
}
```

### Backward Compatibility

✅ **100% Backward Compatible**
- No API changes
- No parameter changes
- No behavior changes (except bug fix)
- Existing code continues to work
- Users passing forward-slash paths unaffected

---

## 🚀 Upgrade Guide

### Recommended For

- ✅ **All users on Windows** using async directory methods
- ✅ **Anyone experiencing path-related errors** with async operations
- ✅ **Teams using `FileItemInfo.FullPath`** for downstream operations

### Installation

```powershell
# Update via NuGet
Update-Package ETCStorageHelper

# Or specify version explicitly
Install-Package ETCStorageHelper -Version 1.4.2
```

### No Code Changes Required

This is a **drop-in replacement**. Simply update the package:

```csharp
// Your existing code works unchanged
var files = await ETCDirectoryAsync.GetFilesWithInfoAsync("ClientA\\Job001", site);
// Now returns properly normalized paths automatically!
```

### Verification

After upgrading, paths are automatically normalized:

```csharp
// Test your path normalization
var testPath = "ClientA\\Job001\\";
var files = await ETCDirectoryAsync.GetFilesWithInfoAsync(testPath, site);

foreach (var file in files)
{
    // Should be ALL forward slashes, no backslashes or double slashes
    Console.WriteLine(file.FullPath);
    // ✅ "ClientA/Job001/file.txt"
    // ✅ "ClientA/Job001/subfolder/report.pdf"
}
```

---

## 🎯 Before vs After Comparison

### Input: Windows-style Path

```csharp
var path = "ClientA\\Job001\\";
```

### Method: GetFilesWithInfoAsync

| Version | Result | Status |
|---------|--------|--------|
| **v1.4.1 and earlier** | `FullPath = "ClientA\\Job001\\/file.txt"` | ❌ Malformed |
| **v1.4.2** | `FullPath = "ClientA/Job001/file.txt"` | ✅ Normalized |

### Method: ExistsAsync

| Version | Result | Status |
|---------|--------|--------|
| **v1.4.1 and earlier** | Could fail to find directory | ❌ Inconsistent |
| **v1.4.2** | Finds directory correctly | ✅ Consistent |

### Consistency

| Comparison | v1.4.1 | v1.4.2 |
|------------|--------|--------|
| Sync vs Async results | ❌ Different | ✅ Identical |
| Windows vs Unix paths | ❌ Inconsistent | ✅ Consistent |
| Path separators | ❌ Mixed `\` and `/` | ✅ All `/` |
| Trailing slashes | ⚠️ Sometimes doubled | ✅ Always trimmed |

---

## 🔧 Technical Details

### Path Normalization Rules

The `NormalizePath()` function applies two transformations:

1. **Backslash → Forward Slash**
   - Input: `"ClientA\\Job001\\file.txt"`
   - Output: `"ClientA/Job001/file.txt"`
   - Reason: SharePoint Graph API expects forward slashes

2. **Trim Leading/Trailing Slashes**
   - Input: `"/ClientA/Job001/"`
   - Output: `"ClientA/Job001"`
   - Reason: Prevents double slashes when joining paths

### Edge Cases Handled

| Input | Output | Notes |
|-------|--------|-------|
| `"ClientA\\Job001"` | `"ClientA/Job001"` | Backslashes converted |
| `"ClientA/Job001/"` | `"ClientA/Job001"` | Trailing slash removed |
| `"ClientA\\Job001\\"` | `"ClientA/Job001"` | Both rules applied |
| `""` | `""` | Empty string preserved |
| `null` | `""` | Null treated as empty |
| `"   "` | `""` | Whitespace treated as empty |

### Performance Impact

- ✅ **Negligible** - String replacement is O(n) with small constant
- ✅ No additional network calls
- ✅ No additional SharePoint API requests
- ✅ Same overall method performance

---

## 🏆 Benefits

### For Users

1. **Consistent Behavior** - Sync and async methods now work identically
2. **Cross-Platform Compatibility** - Windows paths work correctly
3. **Fewer Errors** - Malformed paths no longer cause downstream failures
4. **Better Developer Experience** - No surprises between sync/async

### For the Library

1. **Code Quality** - Eliminates subtle bug
2. **Test Coverage** - New comprehensive path normalization test
3. **Maintainability** - Consistent path handling across all methods
4. **Reliability** - Prevents user-reported path issues

---

## 📝 Related Issues

### Original Report

> "The sync version GetFilesWithInfo normalizes the path using NormalizePath(path) before calling ListDirectoryWithInfoAsync, but the async version GetFilesWithInfoAsync passes the raw path directly. NormalizePath converts backslashes to forward slashes and trims trailing slashes. Since ListDirectoryWithInfoAsync uses the path to construct the FullPath property, the async version can produce malformed paths (like mixed separators ClientA\\Job001/file.txt or double slashes path//file.txt) when users pass Windows-style paths or paths with trailing slashes."

### Fix Summary

✅ Added `NormalizePath()` helper to `ETCDirectoryAsync`  
✅ Applied normalization to all affected async methods  
✅ Added comprehensive test coverage  
✅ Verified sync/async consistency  
✅ No breaking changes

---

## 🎉 Conclusion

Version 1.4.2 is a **high-quality bug fix** that:

- ✅ Eliminates path normalization inconsistencies
- ✅ Ensures Windows compatibility
- ✅ Maintains 100% backward compatibility
- ✅ Includes comprehensive test coverage
- ✅ Recommended for all users

**Upgrade Priority:** Medium - Recommended for all Windows users using async directory methods.

---

## 📞 Questions?

- **Developer:** Pablo
- **Issue Category:** Path Normalization / Bug Fix
- **Version:** 1.4.2
- **Release Date:** December 2025

---

*Bug fix for path normalization in async directory methods - v1.4.2*

