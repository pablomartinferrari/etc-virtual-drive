using System;
using System.Linq;

namespace ETCStorageHelper
{
    /// <summary>
    /// Provides path manipulation methods for SharePoint paths,
    /// similar to System.IO.Path but designed for SharePoint virtual paths.
    /// </summary>
    public static class ETCPath
    {
        /// <summary>
        /// Combines path segments into a single path (using forward slashes for SharePoint)
        /// </summary>
        public static string Combine(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                throw new ArgumentException("At least one path must be provided", nameof(paths));

            // Filter out null or empty segments
            var validPaths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            
            if (validPaths.Length == 0)
                throw new ArgumentException("At least one non-empty path must be provided", nameof(paths));

            // Join with forward slash and normalize
            var combined = string.Join("/", validPaths.Select(p => p.Trim('/', '\\')));
            
            // Remove double slashes
            while (combined.Contains("//"))
            {
                combined = combined.Replace("//", "/");
            }

            return combined;
        }

        /// <summary>
        /// Gets the directory name from a path
        /// </summary>
        public static string GetDirectoryName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            // Normalize to forward slashes
            path = path.Replace('\\', '/').Trim('/');

            var lastSlash = path.LastIndexOf('/');
            if (lastSlash < 0)
                return ""; // No directory, it's a root file

            return path.Substring(0, lastSlash);
        }

        /// <summary>
        /// Gets the file name from a path
        /// </summary>
        public static string GetFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            // Normalize to forward slashes
            path = path.Replace('\\', '/').Trim('/');

            var lastSlash = path.LastIndexOf('/');
            if (lastSlash < 0)
                return path; // No directory, entire path is the filename

            return path.Substring(lastSlash + 1);
        }

        /// <summary>
        /// Gets the file extension from a path
        /// </summary>
        public static string GetExtension(string path)
        {
            var fileName = GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
                return "";

            var lastDot = fileName.LastIndexOf('.');
            if (lastDot < 0)
                return "";

            return fileName.Substring(lastDot);
        }

        /// <summary>
        /// Gets the file name without extension
        /// </summary>
        public static string GetFileNameWithoutExtension(string path)
        {
            var fileName = GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
                return "";

            var lastDot = fileName.LastIndexOf('.');
            if (lastDot < 0)
                return fileName;

            return fileName.Substring(0, lastDot);
        }

        /// <summary>
        /// Check if path has extension
        /// </summary>
        public static bool HasExtension(string path)
        {
            return !string.IsNullOrWhiteSpace(GetExtension(path));
        }

        /// <summary>
        /// Change the extension of a path
        /// </summary>
        public static string ChangeExtension(string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            var directory = GetDirectoryName(path);
            var fileNameWithoutExt = GetFileNameWithoutExtension(path);
            
            if (string.IsNullOrWhiteSpace(extension))
                return Combine(directory, fileNameWithoutExt);

            // Ensure extension starts with a dot
            if (!extension.StartsWith("."))
                extension = "." + extension;

            return Combine(directory, fileNameWithoutExt + extension);
        }
    }
}

