using System;

namespace SentriPet
{
    /// <summary>Name and version.</summary>
    static class AppInfo
    {
        public const string Name = "SentriPet";
        public const string Version = "2.2.1";
    }

    /// <summary>Which operating system we run on.</summary>
    static class Os
    {
        public static readonly bool Windows = OperatingSystem.IsWindows();
        public static readonly bool Mac = OperatingSystem.IsMacOS();
        public static readonly bool Linux = OperatingSystem.IsLinux();

        public static string Name { get { return Windows ? "Windows" : Mac ? "macOS" : Linux ? "Linux" : "Unix"; } }
    }
}
