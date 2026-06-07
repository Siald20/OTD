using Avalonia.Controls;

namespace OTD.Controlls.InterlockingEnlements;

public partial class TrackTiles : UserControl
{
    public TrackTiles()
    {
        InitializeComponent();
    }

    public object? SymbolContent
    {
        get => SymbolHost.Content;
        set => SymbolHost.Content = value;
    }
}
