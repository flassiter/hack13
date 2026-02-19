using Hack13.TerminalServer.Navigation;

namespace Hack13.TerminalServer.Tests;

public class NavigationConfigTests
{
    private static string GetConfigPath()
    {
        // Walk up from test execution directory to find project root
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "configs", "navigation.json");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new FileNotFoundException("Cannot find configs/navigation.json");
    }

    [Fact]
    public void LoadFromFile_ParsesNavigationJson()
    {
        var path = GetConfigPath();
        var config = NavigationConfig.LoadFromFile(path);

        Assert.Equal("sign_on", config.InitialScreen);
        Assert.True(config.Transitions.Count > 0);
        Assert.True(config.Credentials.Count > 0);
    }

    [Fact]
    public void LoadFromFile_HasExpectedTransitions()
    {
        var path = GetConfigPath();
        var config = NavigationConfig.LoadFromFile(path);

        // Should have sign_on -> loan_inquiry transition
        var signOnEnter = config.Transitions
            .FirstOrDefault(t => t.SourceScreen == "sign_on" && t.AidKey == "Enter" && t.TargetScreen == "loan_inquiry");
        Assert.NotNull(signOnEnter);
        Assert.Equal("credentials", signOnEnter.Validation);
    }

    [Fact]
    public void LoadFromFile_HasExpectedCredentials()
    {
        var path = GetConfigPath();
        var config = NavigationConfig.LoadFromFile(path);

        var testUser = config.Credentials.FirstOrDefault(c => c.UserId == "TESTUSER");
        Assert.NotNull(testUser);
        Assert.Equal("TEST1234", testUser.Password);
    }
}
