using System;

namespace ETCStorageHelper
{
    /// <summary>
    /// Represents file information from SharePoint, including name and last modified date
    /// </summary>
    public class ETCFileInfo
    {
        /// <summary>
        /// File name (relative path from the directory being listed)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Last modified date and time in UTC
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// File size in bytes (if available)
        /// </summary>
        public long? Size { get; set; }

        /// <summary>
        /// Whether this item is a folder
        /// </summary>
        public bool IsFolder { get; set; }

        /// <summary>
        /// Full path to the file (directory path + name)
        /// </summary>
        public string FullPath { get; set; }
    }
}

