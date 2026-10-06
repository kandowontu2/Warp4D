namespace Warp4D.Emulation;

internal static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            string? overridePath = Environment.GetEnvironmentVariable("WARP4D_HOME");
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Warp4D")
                : Path.GetFullPath(overridePath);
        }
    }
}
