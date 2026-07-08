using System.Xml;

namespace Charta.FontDiscovery;

/// <summary>
/// Enumerates the platform's font directories without native calls: well-known folders on Windows
/// and macOS, and a minimal read of fontconfig's XML configuration on Linux.
/// </summary>
internal static class SystemFontDirectories
{
    public static IReadOnlyList<string> Get()
    {
        var directories = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"));
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (localAppData.Length > 0)
            {
                directories.Add(Path.Combine(localAppData, "Microsoft", "Windows", "Fonts"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            directories.Add("/System/Library/Fonts");
            directories.Add("/Library/Fonts");
            AddHomeRelative(directories, "Library/Fonts");
        }
        else
        {
            AddFontconfigDirectories(directories, "/etc/fonts/fonts.conf", depth: 0);
            directories.Add("/usr/share/fonts");
            directories.Add("/usr/local/share/fonts");
            AddHomeRelative(directories, ".fonts");
            AddHomeRelative(directories, ".local/share/fonts");
        }

        return directories.Distinct(StringComparer.Ordinal).Where(Directory.Exists).ToList();
    }

    private static void AddHomeRelative(List<string> directories, string relative)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
        {
            directories.Add(Path.Combine(home, relative));
        }
    }

    /// <summary>Reads &lt;dir&gt; entries (and follows &lt;include&gt; one level of globbing) from fontconfig XML.</summary>
    private static void AddFontconfigDirectories(List<string> directories, string configPath, int depth)
    {
        if (depth > 4 || !File.Exists(configPath))
        {
            return;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(configPath, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.Name == "dir")
                {
                    var prefix = reader.GetAttribute("prefix");
                    var value = reader.ReadElementContentAsString().Trim();
                    if (prefix == "xdg")
                    {
                        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        var baseDir = !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(home, ".local", "share");
                        value = Path.Combine(baseDir, value);
                    }
                    else if (value.StartsWith("~/", StringComparison.Ordinal))
                    {
                        value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..]);
                    }

                    if (value.Length > 0)
                    {
                        directories.Add(value);
                    }
                }
                else if (reader.Name == "include")
                {
                    var include = reader.ReadElementContentAsString().Trim();
                    if (include.StartsWith("~/", StringComparison.Ordinal))
                    {
                        include = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), include[2..]);
                    }
                    else if (!Path.IsPathRooted(include))
                    {
                        include = Path.Combine(Path.GetDirectoryName(configPath) ?? "/etc/fonts", include);
                    }

                    if (Directory.Exists(include))
                    {
                        foreach (var file in Directory.EnumerateFiles(include, "*.conf").Order(StringComparer.Ordinal))
                        {
                            AddFontconfigDirectories(directories, file, depth + 1);
                        }
                    }
                    else
                    {
                        AddFontconfigDirectories(directories, include, depth + 1);
                    }
                }
            }
        }
        catch (XmlException)
        {
            // Malformed configuration: fall back to the hardcoded defaults added by the caller.
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
