namespace WeChatRelay.Services;

public static class AppPaths
{
    public const string DirectoryName = "wechat-relay";
    public const string ConfigFileName = "appsettings.json";

    public static string RootDirectory { get; } = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        DirectoryName));

    public static string ConfigFilePath => Path.Combine(RootDirectory, ConfigFileName);

    public static string SessionStatePath => Path.Combine(RootDirectory, "session-state.json");

    public static string InboundMediaDirectory => Path.Combine(RootDirectory, "inbound-media");

    public static string ResolveRootedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return RootDirectory;

        var expandedPath = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(Path.IsPathRooted(expandedPath)
            ? expandedPath
            : Path.Combine(RootDirectory, expandedPath));
    }
}
