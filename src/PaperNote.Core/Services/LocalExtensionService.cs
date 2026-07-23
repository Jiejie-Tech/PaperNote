using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PaperNote.Core.Services;

public sealed class LocalExtensionManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "本地扩展";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public List<LocalTextCommand> Commands { get; set; } = [];
}

public sealed class LocalTextCommand
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "文字处理";
    public string Operation { get; set; } = "Prefix";
    public string Argument { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
}

/// <summary>Safe offline extension host. Extensions are declarative JSON packs and never execute third-party code.</summary>
public sealed class LocalExtensionService
{
    public const int MaximumExtensions = 32;
    public const int MaximumCommandsPerExtension = 32;
    public const int MaximumInputCharacters = 2_000_000;
    public const int MaximumOutputCharacters = 4_000_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public LocalExtensionService(string? directory = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _directory = Path.GetFullPath(directory ?? Path.Combine(local, "PaperNote", "extensions"));
    }

    public async Task<IReadOnlyList<LocalExtensionManifest>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return [];
        var result = new List<LocalExtensionManifest>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(await File.ReadAllTextAsync(file, cancellationToken), _json);
                if (manifest is not null && Normalize(manifest)) result.Add(manifest);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
            if (result.Count >= MaximumExtensions) break;
        }
        return result;
    }

    public async Task<LocalExtensionManifest> ImportAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("找不到扩展包。", packagePath);
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("扩展包缺少 manifest.json。");
        if (entry.Length > 512_000) throw new InvalidDataException("扩展清单过大。");
        await using var stream = entry.Open();
        LocalExtensionManifest manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<LocalExtensionManifest>(stream, _json, cancellationToken)
                ?? throw new InvalidDataException("扩展清单无效。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("扩展清单不是有效的 JSON。", exception);
        }
        if (!Normalize(manifest)) throw new InvalidDataException("扩展清单字段、操作或安全限制无效。");

        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, manifest.Id + ".json");
        if (!File.Exists(target) && Directory.EnumerateFiles(_directory, "*.json").Take(MaximumExtensions).Count() >= MaximumExtensions)
            throw new InvalidDataException($"最多只能安装 {MaximumExtensions} 个本地扩展。");

        var temporary = target + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest, _json), cancellationToken);
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return manifest;
    }

    public bool Remove(string extensionId)
    {
        var safe = NormalizeId(extensionId);
        if (safe.Length == 0) return false;
        var path = Path.Combine(_directory, safe + ".json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public static string Execute(LocalTextCommand command, string? input)
    {
        ArgumentNullException.ThrowIfNull(command);
        var text = input ?? string.Empty;
        if (text.Length > MaximumInputCharacters) throw new InvalidOperationException("扩展输入内容超过安全长度限制。");

        var result = command.Operation switch
        {
            "Uppercase" => text.ToUpperInvariant(),
            "Lowercase" => text.ToLowerInvariant(),
            "Prefix" => command.Argument + text,
            "Suffix" => text + command.Argument,
            "Replace" when !string.IsNullOrEmpty(command.Argument) => text.Replace(command.Argument, command.Replacement ?? string.Empty, StringComparison.Ordinal),
            "RegexReplace" when !string.IsNullOrEmpty(command.Argument) => Regex.Replace(text, command.Argument, command.Replacement ?? string.Empty, RegexOptions.CultureInvariant, RegexTimeout),
            "Replace" or "RegexReplace" => throw new InvalidOperationException("替换操作不能为空。"),
            _ => throw new InvalidOperationException("不支持的扩展操作。")
        };

        if (result.Length > MaximumOutputCharacters) throw new InvalidOperationException("扩展输出内容超过安全长度限制。");
        return result;
    }

    private static bool Normalize(LocalExtensionManifest manifest)
    {
        manifest.Id = NormalizeId(manifest.Id);
        if (manifest.Id.Length == 0) return false;
        manifest.Name = Limit(manifest.Name, 80, "本地扩展");
        manifest.Version = Limit(manifest.Version, 32, "1.0.0");
        manifest.Description = Limit(manifest.Description, 500, string.Empty);
        manifest.Commands ??= [];
        if (manifest.Commands.Count > MaximumCommandsPerExtension || manifest.Commands.Any(item => item is null)) return false;

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in manifest.Commands)
        {
            command.Id = NormalizeId(command.Id);
            if (command.Id.Length == 0 || !ids.Add(command.Id)) return false;
            command.Name = Limit(command.Name, 80, "文字处理");
            if (command.Operation is not ("Uppercase" or "Lowercase" or "Prefix" or "Suffix" or "Replace" or "RegexReplace")) return false;
            command.Argument = LimitRaw(command.Argument, 500);
            command.Replacement = LimitRaw(command.Replacement, 500);
            if (command.Operation is "Replace" or "RegexReplace" && command.Argument.Length == 0) return false;
            if (command.Operation == "RegexReplace")
            {
                try { _ = new Regex(command.Argument, RegexOptions.CultureInvariant, RegexTimeout); }
                catch (ArgumentException) { return false; }
            }
        }
        return true;
    }

    private static string NormalizeId(string? value)
    {
        var text = Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), "[^a-z0-9._-]", "-");
        return text[..Math.Min(text.Length, 80)].Trim('-');
    }

    private static string Limit(string? value, int length, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text[..Math.Min(text.Length, length)];
    }

    private static string LimitRaw(string? value, int length)
    {
        var text = value ?? string.Empty;
        return text[..Math.Min(text.Length, length)];
    }
}
