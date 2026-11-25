using System.Text.Json;
using Bd.Core.BdCli;

namespace Bd.Core.Config;

public interface IAppConfigStore
{
    string FilePath { get; }

    Task<AppConfig> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppConfig config, CancellationToken cancellationToken);
}

/// <summary>配置只写用户 AppData，采用临时文件加替换的原子写，损坏时备份并回退默认值。</summary>
public sealed class JsonAppConfigStore : IAppConfigStore
{
    private readonly string _filePath;

    public JsonAppConfigStore(string? filePath = null) =>
        _filePath = filePath ?? DefaultFilePath();

    public string FilePath => _filePath;

    public static string DefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Bd.Ui",
        "config.json");

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return AppConfig.Default;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return AppConfig.Default;
            }

            var config = JsonSerializer.Deserialize<AppConfig>(json, BdJson.Options);
            return (config ?? AppConfig.Default).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            BackupCorruptedFile();
            return AppConfig.Default;
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        var normalized = config.Normalize();
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(normalized, BdJson.Options);
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        if (File.Exists(_filePath))
        {
            File.Replace(tempPath, _filePath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, _filePath);
        }
    }

    private void BackupCorruptedFile()
    {
        try
        {
            var backup = $"{_filePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Copy(_filePath, backup, overwrite: true);
        }
        catch (IOException)
        {
            // 备份失败不影响回到默认配置。
        }
    }
}
