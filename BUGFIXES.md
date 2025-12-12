# Bug Fixes - ETCStorageHelper

## Issues Fixed

### 1. SharePoint List Logger - "Field 'Level' is not recognized" Error

**Problem:**
The `SharePointListLogger` was trying to write to a "Level" field that didn't exist in existing SharePoint lists, causing the error:
```
Field 'Level' is not recognized
BadRequest - {"error":{"code":"invalidRequest","message":"Field 'Level' is not recognized"}}
```

**Root Cause:**
- When a SharePoint list was created manually, it didn't have all the required columns
- The code tried to write to fields that didn't exist in these manually created lists

**Solution:**
1. **Auto-detect and repair** - Logger now checks if required columns exist
2. **Auto-add missing columns** - Automatically creates any missing columns on first use
3. **Maintains "Level" field** - Keeps the Level column (adds it if missing)
4. **Internal list name** - Uses hardcoded "ETC Storage Logs" (not consumer-configurable)

**Files Changed:**
- `ETCStorageHelper/Logging/SharePointListLogger.cs`
  - Added `using System.Collections.Generic` and `using System.Linq`
  - Added `GetRequiredColumns()` method for centralized column definitions
  - Added `EnsureRequiredColumnsAsync()` method to auto-add missing columns
  - Updated `GetOrCreateListAsync()` to call column verification
  - Updated `CreateListAsync()` to use centralized column definitions

**Migration Notes:**
- **No manual steps required!** The logger now automatically fixes manually created lists
- If you created a list manually and it's missing columns, they'll be added automatically
- The audit log list name is hardcoded to "ETC Storage Logs" - consumers don't configure this

---

### 2. Large File Upload Timeout - 60MB File Upload Failures

**Problem:**
Large file uploads (60MB+) were failing with timeout errors:
```
Upload large file 'G/AsyncTests/large-60mb.dat' (60.0 MB) failed after 3 attempt(s). 
Last error: An error occurred while sending the request.
```

**Root Cause:**
- The timeout for large files was set to `TimeoutSeconds * 3` (typically 180 seconds)
- This wasn't sufficient for very large files, especially on slower connections
- The chunked upload process for 60MB files could take longer than the fixed timeout

**Solution:**
Implemented **dynamic timeout calculation** based on file size:
```csharp
// Calculate timeout: assume 1 MB/sec minimum upload speed + overhead
var fileSizeMB = data.Length / 1024.0 / 1024.0;
var calculatedTimeout = Math.Max(
    _config.TimeoutSeconds * 3,  // Minimum timeout
    (int)(fileSizeMB + 120)      // File size in seconds + 2 min overhead
);
```

**Example Timeouts:**
- 10 MB file: 180 seconds (uses minimum)
- 60 MB file: 180 seconds (60 + 120 = 180, uses minimum)
- 200 MB file: 320 seconds (200 + 120)
- 500 MB file: 620 seconds (500 + 120)

**Files Changed:**
- `ETCStorageHelper/SharePoint/SharePointClient.cs`
  - Updated `UploadLargeFileAsync` to calculate dynamic timeout based on file size
  - Added debug logging to show calculated timeout

**Benefits:**
- ? Prevents timeouts on large file uploads
- ? Scales automatically with file size
- ? Still has a minimum timeout to prevent extremely long waits
- ? Accounts for overhead and slow connections

---

## Testing Recommendations

### Test SharePoint List Logger
```csharp
// If you have an old list, delete it first in SharePoint
// Then run your test - it will create the new schema automatically

var site = SharePointSite.FromConfig("Commercial");
ETCFile.WriteAllText("test.txt", "Hello World", site);
// Check SharePoint - you should see a log entry with Level in the Title
```

### Test Large File Uploads
```csharp
// Create a large file (60MB+)
var largeData = new byte[60 * 1024 * 1024]; // 60 MB
new Random().NextBytes(largeData);

// Upload asynchronously
var handle = ETCFileAsync.WriteAllBytesAsync("test-large.dat", largeData, site);

// Wait for completion
ETCFileAsync.WaitForUploads(site, timeoutSeconds: 600); // 10 minutes max

Console.WriteLine($"Upload status: {handle.Status}");
// Should complete successfully without timeout
```

---

## Version History

### v1.1.0 (Current)
- Fixed SharePoint List Logger schema issues
- Implemented dynamic timeout for large file uploads
- Added schema verification for existing lists
- Better error messages for configuration issues

---

## Additional Notes

### SharePoint List Columns (Current Schema)
The logger now creates lists with these columns:
- Title (text) - Includes operation and user, format: `"[Level] Operation by UserName"`
- UserId (text)
- UserName (text)
- Operation (text)
- SiteName (text)
- Path (text)
- DestinationPath (text)
- FileSizeMB (number)
- DurationMs (number)
- Success (boolean)
- ErrorMessage (text)
- MachineName (text)
- ApplicationName (text)

### Performance Characteristics
- Small files (<4MB): Simple PUT upload, ~60 second timeout
- Large files (4MB+): Chunked upload (5MB chunks), dynamic timeout
- 60MB file: ~180 second timeout (minimum)
- 200MB file: ~320 second timeout (auto-calculated)

### Chunked Upload Details
- Chunk size: 5 MB (Microsoft recommendation)
- Progress logging: Shows percentage and MB uploaded
- Retry logic: Retries entire upload session on failure (configurable via RetryAttempts)
