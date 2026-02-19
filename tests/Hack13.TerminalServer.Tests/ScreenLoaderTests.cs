using Hack13.TerminalServer.Engine;

namespace Hack13.TerminalServer.Tests;

public class ScreenLoaderTests
{
    private static string GetScreenCatalogDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "configs", "screen-catalog");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException("Cannot find configs/screen-catalog/");
    }

    [Fact]
    public void LoadFromDirectory_LoadsAllScreens()
    {
        var loader = new ScreenLoader();
        loader.LoadFromDirectory(GetScreenCatalogDir());

        Assert.Equal(4, loader.AllScreens.Count);
        Assert.True(loader.AllScreens.ContainsKey("sign_on"));
        Assert.True(loader.AllScreens.ContainsKey("loan_inquiry"));
        Assert.True(loader.AllScreens.ContainsKey("loan_details"));
        Assert.True(loader.AllScreens.ContainsKey("escrow_analysis"));
    }

    [Fact]
    public void GetScreen_ReturnsCorrectScreen()
    {
        var loader = new ScreenLoader();
        loader.LoadFromDirectory(GetScreenCatalogDir());

        var screen = loader.GetScreen("sign_on");

        Assert.Equal("sign_on", screen.ScreenId);
        Assert.True(screen.Fields.Count >= 2); // user_id + password
        Assert.True(screen.StaticText.Count > 0);
    }

    [Fact]
    public void GetScreen_SignOn_HasInputFields()
    {
        var loader = new ScreenLoader();
        loader.LoadFromDirectory(GetScreenCatalogDir());

        var screen = loader.GetScreen("sign_on");
        var inputFields = screen.Fields.Where(f => f.Type == "input").ToList();

        Assert.Equal(2, inputFields.Count);
        Assert.Contains(inputFields, f => f.Name == "user_id");
        Assert.Contains(inputFields, f => f.Name == "password");
    }

    [Fact]
    public void GetScreen_LoanDetails_HasDisplayFields()
    {
        var loader = new ScreenLoader();
        loader.LoadFromDirectory(GetScreenCatalogDir());

        var screen = loader.GetScreen("loan_details");
        var displayFields = screen.Fields.Where(f => f.Type == "display").ToList();

        Assert.True(displayFields.Count >= 10);
        Assert.Contains(displayFields, f => f.Name == "loan_number");
        Assert.Contains(displayFields, f => f.Name == "borrower_name");
        Assert.Contains(displayFields, f => f.Name == "current_balance");
    }

    [Fact]
    public void GetScreen_MissingScreen_ThrowsKeyNotFound()
    {
        var loader = new ScreenLoader();
        loader.LoadFromDirectory(GetScreenCatalogDir());

        Assert.Throws<KeyNotFoundException>(() => loader.GetScreen("nonexistent"));
    }

    [Fact]
    public void TryGetScreen_ReturnsTrue_WhenExists()
    {
        var loader = new ScreenLoader();
        loader.LoadFromDirectory(GetScreenCatalogDir());

        Assert.True(loader.TryGetScreen("sign_on", out var screen));
        Assert.NotNull(screen);
    }

    [Fact]
    public void TryGetScreen_ReturnsFalse_WhenMissing()
    {
        var loader = new ScreenLoader();
        loader.LoadFromDirectory(GetScreenCatalogDir());

        Assert.False(loader.TryGetScreen("nonexistent", out _));
    }
}
