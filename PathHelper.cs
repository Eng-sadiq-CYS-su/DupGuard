using System.IO;

namespace DupGuard.Utilities
{
    /// <summary>
    /// Utilities for handling Windows long paths
    /// </summary>
    public static class PathHelper
    {
        private const string LongPathPrefix = @"\\?\";

        /// <summary>
        /// Ensures a path supports long paths on Windows
        /// </summary>
        public static string EnsureLongPathSupport(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Convert relative paths to absolute
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(path);

            // Add long path prefix if not already present and path is long
            if (!path.StartsWith(LongPathPrefix) && path.Length >= 260)
            {
                // Ensure it's an absolute path before adding prefix
                if (Path.IsPathRooted(path))
                {
                    path = LongPathPrefix + path;
                }
            }

            return path;
        }

        /// <summary>
        /// Removes the long path prefix for display purposes
        /// </summary>
        public static string RemoveLongPathPrefix(string path)
        {
            if (path.StartsWith(LongPathPrefix))
                return path.Substring(LongPathPrefix.Length);

            return path;
        }

        /// <summary>
        /// Gets a display-friendly path
        /// </summary>
        public static string GetDisplayPath(string path)
        {
            var displayPath = RemoveLongPathPrefix(path);

            // Truncate very long paths for display
            if (displayPath.Length > 100)
            {
                return "..." + displayPath.Substring(displayPath.Length - 97);
            }

            return displayPath;
        }

        /// <summary>
        /// Checks if a path is a network path
        /// </summary>
        public static bool IsNetworkPath(string path)
        {
            return path.StartsWith(@"\\");
        }

        /// <summary>
        /// Normalizes a path for consistent comparison
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Convert to absolute path
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(path);

            // Remove trailing directory separator
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
