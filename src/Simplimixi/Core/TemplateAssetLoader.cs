using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    internal static class TemplateAssetLoader
    {
        private static readonly byte[] Magic = { 0x53, 0x4D, 0x54, 0x50, 1 };
        private static readonly byte[] Key = CreateKey();

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

        private static byte[] CreateKey()
        {
            byte[] seed =
            {
                0x31, 0xA4, 0x5C, 0x27, 0xE8, 0x09, 0xD3, 0x76,
                0x42, 0xBD, 0x18, 0xC1, 0x6F, 0x90, 0x2A, 0x55,
                0xCE, 0x03, 0xB7, 0x64, 0x1D, 0x88, 0xF2, 0x0B,
                0x79, 0xE1, 0x34, 0xAC, 0x5A, 0x17, 0xC9, 0x60
            };
            byte[] mask =
            {
                0x4F, 0x12, 0xE0, 0x99, 0x3B, 0xC6, 0x70, 0x2D,
                0x84, 0x5E, 0xA9, 0x01, 0xF3, 0x6C, 0x1A, 0xD5
            };

            byte[] key = new byte[24];
            for (int i = 0; i < key.Length; i++)
            {
                int mixed = seed[i] ^ mask[(i * 7 + 3) % mask.Length] ^ (i * 29 + 0x41);
                key[i] = (byte)((mixed << 3) | (mixed >> 5));
            }

            return key;
        }
    }
}
