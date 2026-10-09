using System.Reflection;

namespace HolidayLights.Tests.Contracts;

public sealed class AppContractsTests
{
    [Fact]
    public void HelpContents_ListEveryHelpTopicExactlyOnce()
    {
        HelpContents contents = HelpContentStore.LoadContents();
        string[] contentIds = [.. contents.Books.SelectMany(b => b.Topics).Select(t => t.Id)];
        string[] declaredIds = [.. typeof(HelpTopics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)];

        Assert.Equal(contentIds.Length, contentIds.Distinct().Count());
        Assert.Equal(declaredIds.Order(), contentIds.Order());
        Assert.Equal(
            ["Getting Started", "Arranging the Bulbs", "Playing Background Music", "Using the Screen Saver", "Holiday Lights Themes", "When the Lights Rest", "Keyboard Shortcuts", "Troubleshooting", "Credits"],
            contents.Books.Select(b => b.Title));
    }

    [Fact]
    public void HelpTopics_MapEveryPage() =>
        Assert.All(Enum.GetValues<SettingsPageId>(), page => Assert.False(string.IsNullOrEmpty(HelpTopics.ForPage(page))));

    [Fact]
    public void AppServicesHost_RequiresInitialization()
    {
        if (!AppServicesHost.IsAvailable)
        {
            Assert.Throws<InvalidOperationException>(() => AppServicesHost.Current);
        }

        Assert.Throws<ArgumentNullException>(() => AppServicesHost.Initialize(null!));
    }
}
