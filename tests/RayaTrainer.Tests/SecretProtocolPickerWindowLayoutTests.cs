using System.Xml.Linq;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class SecretProtocolPickerWindowLayoutTests
{
    [Fact]
    public void PickerWindowUsesTwoRowModAndFactionTabs()
    {
        var document = LoadPickerWindowXaml();

        Assert.NotNull(FindByAttribute(document, "ItemsSource", "{Binding Mods}"));
        Assert.NotNull(FindByAttribute(document, "ItemsSource", "{Binding DataContext.Factions, RelativeSource={RelativeSource AncestorType=Window}}"));
        Assert.NotNull(FindByAttribute(document, "ItemsSource", "{Binding DataContext.FilteredProtocols, RelativeSource={RelativeSource AncestorType=Window}}"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute("ItemsSource")?.Value is "{Binding OfficialSecretProtocolGroups}" or "{Binding ModSecretProtocolGroups}");
    }

    private static XDocument LoadPickerWindowXaml()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "RayaTrainer.App",
            "Views",
            "SecretProtocolPickerWindow.xaml");
        return XDocument.Load(path);
    }

    private static XElement FindByAttribute(XContainer document, string name, string value)
    {
        return document
            .Descendants()
            .Single(element => element.Attribute(name)?.Value == value);
    }
}
