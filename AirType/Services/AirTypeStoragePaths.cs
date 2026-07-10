using System;
using System.IO;

namespace AirType.Services;

public static class AirTypeStoragePaths
{
    public const string AppFolderName = "AirType";
    public const string LegacyAppFolderName = "ModernDictationApp";
    public const string ProcessStorageRootEnvironmentVariable = "AIRTYPE_STORAGE_ROOT";

    internal static string? CanonicalRootOverride { get; set; }

    public static string CanonicalRoot =>
        CanonicalRootOverride ??
        GetProcessStorageRoot() ??
        GetAppFolder(Environment.SpecialFolder.LocalApplicationData, AppFolderName);

    public static string LegacyCanonicalRoot =>
        GetCompatibilityRoot("Local", LegacyAppFolderName) ??
        GetAppFolder(Environment.SpecialFolder.LocalApplicationData, LegacyAppFolderName);

    public static string LegacyRoamingRoot =>
        GetCompatibilityRoot("Roaming", LegacyAppFolderName) ??
        GetAppFolder(Environment.SpecialFolder.ApplicationData, LegacyAppFolderName);

    public static string RoamingCompatibilityRoot =>
        GetCompatibilityRoot("Roaming", AppFolderName) ??
        GetAppFolder(Environment.SpecialFolder.ApplicationData, AppFolderName);

    public static string GetCanonicalPath(params string[] segments) => Combine(CanonicalRoot, segments);

    public static string GetLegacyCanonicalPath(params string[] segments) => Combine(LegacyCanonicalRoot, segments);

    public static string GetLegacyRoamingPath(params string[] segments) => Combine(LegacyRoamingRoot, segments);

    public static string GetRoamingCompatibilityPath(params string[] segments) => Combine(RoamingCompatibilityRoot, segments);

    private static string GetAppFolder(Environment.SpecialFolder specialFolder, string folderName)
    {
        return Path.Combine(Environment.GetFolderPath(specialFolder), folderName);
    }

    private static string? GetProcessStorageRoot()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable(ProcessStorageRootEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? null
            : Path.GetFullPath(configuredRoot.Trim());
    }

    private static string? GetCompatibilityRoot(string scope, string folderName)
    {
        string? processRoot = GetProcessStorageRoot();
        return processRoot == null
            ? null
            : Path.Combine(processRoot, ".compat", scope, folderName);
    }

    private static string Combine(string root, params string[] segments)
    {
        if (segments.Length == 0)
        {
            return root;
        }

        var pathParts = new string[segments.Length + 1];
        pathParts[0] = root;
        Array.Copy(segments, 0, pathParts, 1, segments.Length);
        return Path.Combine(pathParts);
    }
}
