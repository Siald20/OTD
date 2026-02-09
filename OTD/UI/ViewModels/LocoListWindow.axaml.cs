using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OTD.UI.ViewModels;

public partial class LocoListWindow : Window
{
    public LocoListWindow()
    {
        InitializeComponent();
        loco.ItemsSource = LoadLocos()
            .OrderBy(x => x);
    }

    private static string[] LoadLocos()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "loco.xml");
        if (!File.Exists(filePath))
        {
            return Array.Empty<string>();
        }

        var doc = XDocument.Load(filePath);
        var locos = doc.Root?.Elements("loco")
            .Select(loco => (string?)loco.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return locos ?? Array.Empty<string>();
    }
}
