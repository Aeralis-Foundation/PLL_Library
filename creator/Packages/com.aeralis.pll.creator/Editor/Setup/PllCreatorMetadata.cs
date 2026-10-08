using System.Globalization;
using System.Linq;
using System.Security.Cryptography;

namespace PLL.Creator.Editor
{
    internal static class PllCreatorMetadata
    {
        internal static string Script(string Seed)
        {
            return "fileFormatVersion: 2\n"
                + "guid: " + StableGuid(Seed) + "\n"
                + "MonoImporter:\n"
                + "  externalObjects: {}\n"
                + "  serializedVersion: 2\n"
                + "  defaultReferences: []\n"
                + "  executionOrder: 0\n"
                + "  icon: {instanceID: 0}\n"
                + "  userData:\n"
                + "  assetBundleName:\n"
                + "  assetBundleVariant:\n";
        }

        internal static string Text(string Seed)
        {
            return "fileFormatVersion: 2\n"
                + "guid: " + StableGuid(Seed) + "\n"
                + "TextScriptImporter:\n"
                + "  externalObjects: {}\n"
                + "  userData:\n"
                + "  assetBundleName:\n"
                + "  assetBundleVariant:\n";
        }

        internal static string Plugin(string Seed)
        {
            return "fileFormatVersion: 2\n"
                + "guid: " + StableGuid(Seed) + "\n"
                + "PluginImporter:\n"
                + "  externalObjects: {}\n"
                + "  serializedVersion: 2\n"
                + "  iconMap: {}\n"
                + "  executionOrder: {}\n"
                + "  defineConstraints: []\n"
                + "  isPreloaded: 0\n"
                + "  isOverridable: 0\n"
                + "  isExplicitlyReferenced: 0\n"
                + "  validateReferences: 1\n"
                + "  platformData:\n"
                + "  - first:\n"
                + "      Any:\n"
                + "    second:\n"
                + "      enabled: 0\n"
                + "      settings: {}\n"
                + "  - first:\n"
                + "      Editor: Editor\n"
                + "    second:\n"
                + "      enabled: 1\n"
                + "      settings:\n"
                + "        DefaultValueInitialized: true\n"
                + "  - first:\n"
                + "      Standalone: Win64\n"
                + "    second:\n"
                + "      enabled: 1\n"
                + "      settings:\n"
                + "        CPU: x86_64\n"
                + "  userData:\n"
                + "  assetBundleName:\n"
                + "  assetBundleVariant:\n";
        }

        internal static string Folder(string Seed)
        {
            return "fileFormatVersion: 2\n"
                + "guid: " + StableGuid(Seed) + "\n"
                + "folderAsset: yes\n"
                + "DefaultImporter:\n"
                + "  externalObjects: {}\n"
                + "  userData:\n"
                + "  assetBundleName:\n"
                + "  assetBundleVariant:\n";
        }

        static string StableGuid(string Seed)
        {
            using (var Sha = SHA256.Create())
                return string.Concat(Sha.ComputeHash(
                    PllCreatorFileSystem.StrictUtf8.GetBytes("PLL Creator/" + Seed)).Take(16)
                    .Select(Value => Value.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
