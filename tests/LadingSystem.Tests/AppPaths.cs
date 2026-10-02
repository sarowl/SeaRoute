namespace LadingSystem.Tests;

internal static class AppPaths
{
    public static string Root
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "LadingSystem.csproj"))) return directory.FullName;
            throw new DirectoryNotFoundException("LadingSystem.csproj was not found above the test output directory.");
        }
    }
}
