using System.IO;

internal static class SpriteOutputPolicy
{
    public static string FindWorkspace(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ChihDesktop.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Run this development tool from its ChihDesktop checkout.");
    }

    public static string Resolve(string workspace, string? requested)
    {
        string allowed = Path.GetFullPath(Path.Combine(workspace, "artifacts", "LegacySprites"));
        string target = Path.GetFullPath(requested is null ? allowed : Path.Combine(workspace, requested));
        string relative = Path.GetRelativePath(allowed, target);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Legacy SpriteBuilder may write only under artifacts/LegacySprites. Production sprites are protected.");
        // Reject junction/symlink escapes, including existing parents below the workspace.
        for (var directory = new DirectoryInfo(target); directory is not null &&
            !directory.FullName.Equals(Path.GetFullPath(workspace), StringComparison.OrdinalIgnoreCase); directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Legacy sprite output cannot use junctions or symbolic links.");
        return target;
    }

    public static FileStream CreateFile(string workspace, string output, string name)
    {
        if (Path.GetFileName(name) != name) throw new InvalidOperationException("Expected an output filename, not a path.");
        string directory = Resolve(workspace, output);
        // Refuse existing files too: following a file symlink/hardlink could otherwise
        // overwrite production bytes despite a safe directory. Choose a fresh output subfolder.
        return new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }
}
