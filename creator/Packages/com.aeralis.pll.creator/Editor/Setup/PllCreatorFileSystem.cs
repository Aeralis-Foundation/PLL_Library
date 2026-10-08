using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace PLL.Creator.Editor
{
    public static class PllCreatorFileSystem
    {
        public static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static string ProjectRootPath()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        internal static string ExtendedPath(string PathValue)
        {
            var FullPath = Path.GetFullPath(PathValue);
            if (Path.DirectorySeparatorChar != '\\'
                || FullPath.StartsWith(@"\\?\", StringComparison.Ordinal)) return FullPath;
            return FullPath.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + FullPath.Substring(2) : @"\\?\" + FullPath;
        }

        internal static bool TryAssetPathToFullPath(string AssetPath, out string FullPath)
        {
            FullPath = string.Empty;
            try
            {
                FullPath = AssetPathToFullPath(AssetPath);
                return !string.IsNullOrEmpty(FullPath);
            }
            catch (Exception Exception) when (Exception is ArgumentException || Exception is NotSupportedException
                                               || Exception is SecurityException)
            {
                return false;
            }
        }

        public static string AssetPathToFullPath(string AssetPath)
        {
            if (string.IsNullOrEmpty(AssetPath)) return string.Empty;
            if (Path.IsPathRooted(AssetPath)) return Path.GetFullPath(AssetPath);
            var Normalized = AssetPath.Replace('\\', '/');
            if (Normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                var Package = PackageManagerInfo.FindForAssetPath(Normalized);
                if (Package == null) return string.Empty;
                var Prefix = "Packages/" + Package.name;
                var Relative = Normalized.Substring(Prefix.Length).TrimStart('/');
                return Path.GetFullPath(Path.Combine(Package.resolvedPath,
                    Relative.Replace('/', Path.DirectorySeparatorChar)));
            }
            return Path.GetFullPath(Path.Combine(ProjectRootPath(),
                Normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static bool SamePath(string Left, string Right)
        {
            try
            {
                return !string.IsNullOrEmpty(Left) && !string.IsNullOrEmpty(Right)
                    && string.Equals(Path.GetFullPath(Left).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(Right).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception Exception) when (Exception is ArgumentException || Exception is NotSupportedException
                                               || Exception is SecurityException)
            {
                return false;
            }
        }

        public static bool Inside(string PathValue, string Directory)
        {
            var FullPath = Path.GetFullPath(PathValue).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var FullDirectory = Path.GetFullPath(Directory).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            return string.Equals(FullPath, FullDirectory, StringComparison.OrdinalIgnoreCase)
                || FullPath.StartsWith(FullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static string Sha256(string PathValue)
        {
            using (var Stream = File.OpenRead(PathValue))
            using (var Sha = SHA256.Create())
                return string.Concat(Sha.ComputeHash(Stream).Select(Value =>
                    Value.ToString("X2", CultureInfo.InvariantCulture)));
        }

        internal static string Sha256Text(string Value)
        {
            using (var Sha = SHA256.Create())
                return string.Concat(Sha.ComputeHash(StrictUtf8.GetBytes(Value)).Select(Item =>
                    Item.ToString("X2", CultureInfo.InvariantCulture)));
        }
    }
}
