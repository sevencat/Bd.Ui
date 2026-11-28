using Bd.Core.Abstractions;

using MySqlConnector;

namespace Bd.Core.Dolt;

/// <summary>连接探测结果；失败时只带用户可读信息，不抛异常。</summary>
public sealed record DoltConnectionProbe(bool Ok, string Message, string? ServerVersion = null);

/// <summary>
/// 构造到远程 Dolt sql-server 的连接。密码优先取
/// <see cref="DoltConnectionSettings.Password"/>（来自 config.json 或设置页），
/// 为空时回退到环境变量 <see cref="PasswordEnvironmentVariable"/>。
/// </summary>
public sealed class DoltConnectionFactory
{
    public const string PasswordEnvironmentVariable = "BEADS_DOLT_PASSWORD";

    private readonly Func<string?> _readEnvPassword;

    public DoltConnectionFactory(Func<string?>? readPassword = null) =>
        _readEnvPassword = readPassword ?? (static () => Environment.GetEnvironmentVariable(PasswordEnvironmentVariable));

    /// <summary>环境变量里是否提供了密码（用于提示“配置为空时还能靠环境变量兜底”）。</summary>
    public bool HasEnvironmentPassword => !string.IsNullOrWhiteSpace(_readEnvPassword());

    public bool HasPassword(DoltConnectionSettings settings) =>
        settings is not null && !string.IsNullOrWhiteSpace(ResolvePassword(settings));

    public MySqlConnection CreateConnection(DoltConnectionSettings settings) =>
        new(BuildConnectionString(settings));

    public string BuildConnectionString(DoltConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var password = ResolvePassword(settings);
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new BdException(
                BdErrorCode.InvalidArgument,
                $"未配置 Dolt 密码：请在「设置」里填写，或设置环境变量 {PasswordEnvironmentVariable}");
        }

        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            Port = (uint)settings.Port,
            UserID = settings.User,
            Password = password,
            Database = settings.Database,
            SslMode = settings.UseTls ? MySqlSslMode.Preferred : MySqlSslMode.None,
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 30,
            AllowUserVariables = true,
            Pooling = true
        };

        return builder.ConnectionString;
    }

    /// <summary>打开一次连接并读取服务端版本，用于设置页的连接测试。</summary>
    public async Task<DoltConnectionProbe> TestAsync(
        DoltConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            await using var connection = CreateConnection(settings);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT VERSION()";
            var version = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;

            return new DoltConnectionProbe(
                true,
                $"已连接 {settings.Describe()}" + (string.IsNullOrWhiteSpace(version) ? string.Empty : $"（Dolt {version}）"),
                version);
        }
        catch (BdException ex)
        {
            return new DoltConnectionProbe(false, ex.UserMessage);
        }
        catch (MySqlException ex)
        {
            return new DoltConnectionProbe(false, $"连接失败：{ex.Message}");
        }
        catch (TimeoutException ex)
        {
            return new DoltConnectionProbe(false, $"连接超时：{ex.Message}");
        }
    }

    private string? ResolvePassword(DoltConnectionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Password))
        {
            return settings.Password.Trim();
        }

        var env = _readEnvPassword();
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }
}
