using Bd.Core.BdCli;
using Bd.Core.Dolt;
using FluentAssertions;
using MySqlConnector;
using Xunit;

namespace Bd.Core.Tests;

public sealed class DoltConnectionFactoryTests
{
    private static readonly DoltConnectionSettings Settings = new()
    {
        Host = "10.0.0.9",
        Port = 7050,
        User = "nwms",
        Database = "king_bdui"
    };

    [Fact]
    public void Connection_string_carries_server_settings_and_env_password()
    {
        var factory = new DoltConnectionFactory(() => "s3cret");

        var builder = new MySqlConnectionStringBuilder(factory.BuildConnectionString(Settings));

        builder.Server.Should().Be("10.0.0.9");
        builder.Port.Should().Be(7050);
        builder.UserID.Should().Be("nwms");
        builder.Database.Should().Be("king_bdui");
        builder.Password.Should().Be("s3cret");
        builder.SslMode.Should().Be(MySqlSslMode.None);
    }

    [Fact]
    public void Missing_password_is_reported_with_environment_variable_name()
    {
        var factory = new DoltConnectionFactory(static () => null);

        var act = () => factory.BuildConnectionString(Settings);

        act.Should().Throw<BdException>()
            .Which.Code.Should().Be(BdErrorCode.InvalidArgument);
        factory.HasPassword.Should().BeFalse();
    }

    [Fact]
    public async Task Test_reports_failure_instead_of_throwing_when_password_missing()
    {
        var factory = new DoltConnectionFactory(static () => " ");

        var probe = await factory.TestAsync(Settings, CancellationToken.None);

        probe.Ok.Should().BeFalse();
        probe.Message.Should().Contain(DoltConnectionFactory.PasswordEnvironmentVariable);
    }

    [Fact]
    public void Config_snapshot_maps_to_settings()
    {
        var settings = DoltConnectionSettings.FromConfig(new Bd.Core.Config.AppConfig
        {
            DoltHost = "1.15.245.172",
            DoltPort = 7050,
            DoltUser = "nwms",
            DoltDatabase = "king_fh2sharp"
        });

        settings.Describe().Should().Be("nwms@1.15.245.172:7050/king_fh2sharp");
        settings.WithDatabase("king_bdui").Database.Should().Be("king_bdui");
    }
}
