using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>The example theme (#24, examples/themes/cloud), built into the program.</summary>
    static class ExampleTheme
    {
        public const string Folder = "cloud";
        const string Prefix = "SentriPet.ExampleTheme.";

        /// <summary>Writes the example's theme.json and pictures into <paramref name="dir"/>.</summary>
        public static void Install(string dir)
        {
            Directory.CreateDirectory(dir);
            var asm = typeof(ExampleTheme).Assembly;
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix)))
                using (var s = asm.GetManifestResourceStream(name))
                using (var f = File.Create(Path.Combine(dir, name.Substring(Prefix.Length))))
                    s.CopyTo(f);
        }
    }
}
