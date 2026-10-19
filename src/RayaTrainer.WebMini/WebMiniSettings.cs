using System.Text.Encodings.Web;
using System.Text.Json;
using RayaTrainer.Host.Services;

namespace RayaTrainer.WebMini;

/// <summary>
/// WebMini 专属设置：监听端口与绑定网卡。与主程序设置（RayaTrainer.settings.json）
/// 完全隔离——主程序不再消费任何 Web 配置（ADR 0033），老设置文件零迁移。
/// </summary>
public sealed class WebMiniSettings
{
    /// <summary>绑定全部网卡的通配地址（默认）。</summary>
    public const string BindAll = "0.0.0.0";

    public int Port { get; set; } = TrainerWebEndpointDefaults.Port;

    public string BindAddress { get; set; } = BindAll;
}

/// <summary>
/// <see cref="WebMiniSettings"/> 的文件存储：安装目录下 RayaTrainer.WebMini.settings.json，
/// 原子写入（临时文件 + 覆盖替换），损坏/缺失回退默认值。
/// </summary>
public sealed class WebMiniSettingsStore
{
    public const string SettingsFileName = "RayaTrainer.WebMini.settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _path;

    public WebMiniSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, SettingsFileName);
    }

    public WebMiniSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new WebMiniSettings();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<WebMiniSettings>(File.ReadAllText(_path), JsonOptions);
            return Normalize(parsed ?? new WebMiniSettings());
        }
        catch (JsonException)
        {
            return new WebMiniSettings();
        }
        catch (IOException)
        {
            return new WebMiniSettings();
        }
    }

    /// <summary>保存设置；写入失败抛出异常由调用方决定回滚策略（端口热重启失败时不写盘）。</summary>
    public void Save(WebMiniSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings);

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(tempPath, _path, overwrite: true);
    }

    private static WebMiniSettings Normalize(WebMiniSettings settings)
    {
        return new WebMiniSettings
        {
            Port = settings.Port is >= 1 and <= 65535 ? settings.Port : TrainerWebEndpointDefaults.Port,
            BindAddress = string.IsNullOrWhiteSpace(settings.BindAddress)
                ? WebMiniSettings.BindAll
                : settings.BindAddress.Trim()
        };
    }
}
