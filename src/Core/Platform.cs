using System;

namespace SentriPet
{
    /// <summary>Name and version, shared by the Windows (WPF) and the cross-platform (Avalonia) builds.</summary>
    static class AppInfo
    {
        public const string Name = "SentriPet";
        public const string Version = "1.3.0";
    }

    /// <summary>
    /// Which operating system we run on. The WPF build (.NET Framework) only ever runs on Windows; the .NET 10
    /// build asks the runtime.
    /// </summary>
    static class Os
    {
#if NET
        public static readonly bool Windows = OperatingSystem.IsWindows();
        public static readonly bool Mac = OperatingSystem.IsMacOS();
        public static readonly bool Linux = OperatingSystem.IsLinux();
#else
        public static readonly bool Windows = true;
        public static readonly bool Mac = false;
        public static readonly bool Linux = false;
#endif

        public static string Name { get { return Windows ? "Windows" : Mac ? "macOS" : Linux ? "Linux" : "Unix"; } }
    }
}
