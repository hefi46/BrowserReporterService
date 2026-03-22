using BrowserReporterService.Services;

namespace BrowserReporterService.Tests;

public class CommandLineArgsTests
{
    [Fact]
    public void NoArgs_AllDefaultsToFalse()
    {
        var args = new CommandLineArgs([]);

        Assert.False(args.IsDebug);
        Assert.False(args.IsOnce);
        Assert.False(args.Install);
        Assert.False(args.Uninstall);
        Assert.False(args.EncryptConfig);
        Assert.Null(args.ConfigPath);
        Assert.Null(args.ServerUrl);
        Assert.True(args.ShouldRunApplication);
    }

    [Theory]
    [InlineData("--debug", nameof(CommandLineArgs.IsDebug))]
    [InlineData("--once", nameof(CommandLineArgs.IsOnce))]
    [InlineData("--install", nameof(CommandLineArgs.Install))]
    [InlineData("--uninstall", nameof(CommandLineArgs.Uninstall))]
    public void BooleanFlags_SetCorrectly(string flag, string propertyName)
    {
        var args = new CommandLineArgs([flag]);
        var property = typeof(CommandLineArgs).GetProperty(propertyName);
        Assert.True((bool)property!.GetValue(args)!);
    }

    [Fact]
    public void ConfigPath_ParsedFromArgs()
    {
        var args = new CommandLineArgs(["--config", @"C:\path\to\config.json"]);
        Assert.Equal(@"C:\path\to\config.json", args.ConfigPath);
    }

    [Fact]
    public void ServerUrl_ParsedFromArgs()
    {
        var args = new CommandLineArgs(["--server", "http://myserver:8000"]);
        Assert.Equal("http://myserver:8000", args.ServerUrl);
    }

    [Fact]
    public void MultipleFlags_AllParsed()
    {
        var args = new CommandLineArgs(["--debug", "--once", "--server", "http://test:8000"]);
        Assert.True(args.IsDebug);
        Assert.True(args.IsOnce);
        Assert.Equal("http://test:8000", args.ServerUrl);
        Assert.True(args.ShouldRunApplication);
    }

    [Fact]
    public void ShouldRunApplication_FalseWhenInstall()
    {
        var args = new CommandLineArgs(["--install"]);
        Assert.False(args.ShouldRunApplication);
    }

    [Fact]
    public void ShouldRunApplication_FalseWhenUninstall()
    {
        var args = new CommandLineArgs(["--uninstall"]);
        Assert.False(args.ShouldRunApplication);
    }

    [Fact]
    public void EncryptConfig_WithoutConfigPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CommandLineArgs(["--encryptconfig"]));
    }

    [Fact]
    public void EncryptConfig_WithConfigPath_Succeeds()
    {
        var args = new CommandLineArgs(["--encryptconfig", "--config", "test.json"]);
        Assert.True(args.EncryptConfig);
        Assert.Equal("test.json", args.ConfigPath);
        Assert.False(args.ShouldRunApplication);
    }

    [Fact]
    public void NoTrayFlag_NotRecognized()
    {
        // --no-tray has been removed; verify it doesn't crash and has no effect
        var args = new CommandLineArgs(["--no-tray"]);
        Assert.True(args.ShouldRunApplication);
    }
}
