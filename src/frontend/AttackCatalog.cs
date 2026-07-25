using System;
using System.Collections.Generic;
using System.IO;

namespace CvAut
{
    public static class AttackCatalog
    {
        public static IReadOnlyList<string> Discover()
        {
            var names = new List<string>();
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "assets", "Templates", "attacks");
                if (Directory.Exists(dir))
                {
                    foreach (string file in Directory.GetFiles(dir, "*.txt"))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch
            {
                // Best effort — empty catalog means user types attack name.
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
