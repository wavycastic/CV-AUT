using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    internal static class TemplateAssetLoader
    {
        private static readonly byte[] Magic = { (byte)'S', (byte)'M', (byte)'T', (byte)'P', 1 };
        private static readonly byte[] Key =
        {
            0x53, 0x69, 0x6D, 0x70, 0x6C, 0x69, 0x4D, 0x69,
            0x78, 0x69, 0x2D, 0x54, 0x65, 0x6D, 0x70, 0x6C,
            0x61, 0x74, 0x65, 0x73, 0x2D, 0x30, 0x35, 0x31
        };

        public static bool Exists(string templatesRoot, string templateName)
        {
            return File.Exists(GetPlainPath(templatesRoot, templateName))
                || File.Exists(GetEncryptedPath(templatesRoot, templateName));
        }

        public static Mat Load(string templatesRoot, string templateName, ImreadModes mode)
        {
            string encryptedPath = GetEncryptedPath(templatesRoot, templateName);
            if (File.Exists(encryptedPath))
            {
                byte[] encodedBytes = Decode(File.ReadAllBytes(encryptedPath));
                return Cv2.ImDecode(encodedBytes, mode);
            }

            string plainPath = GetPlainPath(templatesRoot, templateName);
            return File.Exists(plainPath) ? Cv2.ImRead(plainPath, mode) : new Mat();
        }

        public static IEnumerable<string> EnumerateNames(string templatesRoot, string subdir)
        {
            string root = Path.Combine(templatesRoot, subdir);
            if (!Directory.Exists(root))
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(root, "*.png", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(root, "*.dat", SearchOption.TopDirectoryOnly)))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (seen.Add(name))
                {
                    yield return name;
                }
            }
        }

        private static string GetPlainPath(string templatesRoot, string templateName)
        {
            string normalizedName = NormalizeTemplateName(templateName, ".png");
            return Path.Combine(templatesRoot, normalizedName);
        }

        private static string GetEncryptedPath(string templatesRoot, string templateName)
        {
            string normalizedName = NormalizeTemplateName(templateName, ".dat");
            return Path.Combine(templatesRoot, normalizedName);
        }

        private static string NormalizeTemplateName(string templateName, string extension)
        {
            string normalizedName = templateName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string currentExtension = Path.GetExtension(normalizedName);
            if (string.Equals(currentExtension, extension, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedName;
            }

            if (string.Equals(currentExtension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(currentExtension, ".dat", StringComparison.OrdinalIgnoreCase))
            {
                return Path.ChangeExtension(normalizedName, extension);
            }

            return normalizedName + extension;
        }

        private static byte[] Decode(byte[] encryptedBytes)
        {
            if (encryptedBytes.Length <= Magic.Length || !encryptedBytes.Take(Magic.Length).SequenceEqual(Magic))
            {
                return encryptedBytes;
            }

            byte[] decoded = new byte[encryptedBytes.Length - Magic.Length];
            for (int i = 0; i < decoded.Length; i++)
            {
                decoded[i] = (byte)(encryptedBytes[i + Magic.Length] ^ Key[i % Key.Length]);
            }

            return decoded;
        }
    }
}
