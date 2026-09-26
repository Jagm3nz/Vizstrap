using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Vizstrap.Core.Roblox;

namespace Vizstrap.Core.Tests.TestSupport;

internal static class TestZip
{
    /// <summary>Builds a zip; names ending in "/" become directory entries, exactly as written.</summary>
    public static byte[] Create(params (string Name, string Content)[] entries)
    {
        using var memory = new MemoryStream();

        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);

                if (name.EndsWith('/'))
                    continue;

                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return memory.ToArray();
    }

    public static string Md5(byte[] bytes) => Convert.ToHexStringLower(MD5.HashData(bytes));
}

/// <summary>
/// A tiny fake Roblox deployment on top of <see cref="FakeHttpHandler"/>, shaped like the real one
/// (leading-slash directory entries, a non-zip installer entry, an ignored and an unknown package).
/// </summary>
internal sealed class FakeRoblox
{
    public const string Mirror = "https://setup.rbxcdn.com";

    private readonly FakeHttpHandler _http;

    public FakeRoblox(FakeHttpHandler http, string versionGuid = "version-aaaa111122223333")
    {
        _http = http;

        _http.MapText($"{Mirror}/versionStudio", RobloxDeployment.VersionStudioHash);

        Publish(versionGuid, new Dictionary<string, byte[]>
        {
            ["RobloxApp.zip"] = TestZip.Create(("/", ""), ("RobloxPlayerBeta.exe", $"player {versionGuid}")),
            ["content-sounds.zip"] = TestZip.Create(("/", ""), ("/sfx/", ""), ("sfx/ouch.ogg", "oof")),
            ["WebView2RuntimeInstaller.zip"] = TestZip.Create(("setup.exe", "webview")),
            ["content-brand-new.zip"] = TestZip.Create(("new.txt", "?")),
            ["RobloxPlayerInstaller.exe"] = Encoding.UTF8.GetBytes("official bootstrapper"),
        });
    }

    public string VersionGuid { get; private set; } = null!;

    public Dictionary<string, byte[]> Packages { get; private set; } = null!;

    public void Publish(string versionGuid, Dictionary<string, byte[]> packages, string version = "0.1.0.1")
    {
        VersionGuid = versionGuid;
        Packages = packages;

        string versionJson = $"{{\"version\":\"{version}\",\"clientVersionUpload\":\"{versionGuid}\",\"bootstrapperVersion\":\"\"}}";
        _http.MapText("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer", versionJson);

        var manifest = new StringBuilder("v0\r\n");

        foreach (var (name, bytes) in packages)
        {
            manifest.Append($"{name}\r\n{TestZip.Md5(bytes)}\r\n{bytes.Length}\r\n{bytes.Length * 2}\r\n");
            _http.MapBytes(PackageUrl(name), bytes);
        }

        _http.MapText($"{Mirror}/channel/common/{versionGuid}-rbxPkgManifest.txt", manifest.ToString());
    }

    public string PackageUrl(string name) => $"{Mirror}/channel/common/{VersionGuid}-{name}";

    public Package PackageInfo(string name)
    {
        byte[] bytes = Packages[name];
        return new Package(name, TestZip.Md5(bytes), bytes.Length, bytes.Length * 2);
    }
}
