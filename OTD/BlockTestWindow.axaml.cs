using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OTD.Controlls.InterlockingEnlements.BlockTiles;

namespace OTD;

public partial class BlockTestWindow : Window
{
    private const int OperationCycles = 12;
    private const int ButtonImpulseCycles = 3;

    private readonly StackPanel _leftControls;
    private readonly StackPanel _rightControls;
    private readonly TextBlock _leftState;
    private readonly TextBlock _rightState;
    private readonly TextBlock _connectionState;
    private readonly BlockTile _leftBlockTile;
    private readonly BlockTile _rightBlockTile;

    private TMN840Pair _left = null!;
    private TMN840Pair _right = null!;
    private int _cycles;

    public BlockTestWindow()
    {
        InitializeComponent();
        _leftControls = Find<StackPanel>("LeftControls");
        _rightControls = Find<StackPanel>("RightControls");
        _leftState = Find<TextBlock>("LeftState");
        _rightState = Find<TextBlock>("RightState");
        _connectionState = Find<TextBlock>("ConnectionState");
        _leftBlockTile = Find<BlockTile>("LeftBlockTile");
        _rightBlockTile = Find<BlockTile>("RightBlockTile");
        CreateSimulation();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"Control '{name}' wurde nicht gefunden.");

    private void CreateSimulation()
    {
        _left = new TMN840Pair();
        _right = new TMN840Pair();
        TMN840Pair.ConnectBlocks(_left, _right);
        _cycles = 0;

        BuildControls(_leftControls, _left.Block);
        BuildControls(_rightControls, _right.Block);
        RunSteps(OperationCycles);
    }

    private void BuildControls(StackPanel panel, TMN840_BS block)
    {
        panel.Children.Clear();
        var actions = new[]
        {
            ("Fahrtrichtung anfordern", block.t_FA),
            ("Vorblocken", block.t_VB),
            ("Rückblocken", block.t_RB),
            ("Blocken", block.t_BL),
            ("Freie Bahn festhalten", block.t_FBH),
            ("Freie Bahn freigeben", block.t_FBF),
            ("Sperre einschalten", block.t_SE),
            ("Sperre aufheben bestätigen", block.t_SA)
        };

        var groupName = $"BlockAction{Guid.NewGuid():N}";
        foreach (var (label, input) in actions)
        {
            var radio = new RadioButton { Content = label, GroupName = groupName };
            radio.IsCheckedChanged += (_, _) => input.Value = radio.IsChecked == true;
            panel.Children.Add(radio);
        }

        var blockButton = new Button
        {
            Content = "BLOCKTASTE",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            MinHeight = 48,
            Margin = new Avalonia.Thickness(0, 8, 0, 0)
        };
        blockButton.Click += (_, _) => PressBlockButton(block);
        panel.Children.Add(blockButton);
    }

    private void PressBlockButton(TMN840_BS block)
    {
        block.t_BT.Value = true;
        RunSteps(ButtonImpulseCycles);
        block.t_BT.Value = false;
        RunSteps(OperationCycles);
    }

    private void RunSteps(int count)
    {
        for (var i = 0; i < count; i++)
        {
            UpdateRelaySet(_left);
            UpdateRelaySet(_right);
            CommitRelaySet(_left);
            CommitRelaySet(_right);
            OutputRelaySet(_left);
            OutputRelaySet(_right);
            _cycles++;
        }

        ShowState();
    }

    private static void UpdateRelaySet(TMN840Pair pair)
    {
        pair.Block.Update();
    }

    private static void OutputRelaySet(TMN840Pair pair)
    {
        pair.Block.Output();
    }

    private static void CommitRelaySet(TMN840Pair pair)
    {
        CommitRelays(pair.Block);
    }

    private static void CommitRelays(object target)
    {
        foreach (var relay in target.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .Select(field => field.GetValue(target))
                     .OfType<Relais>())
        {
            relay.Commit();
        }
    }

    private void ShowState()
    {
        _leftState.Text = DescribeBlock(_left.Block);
        _rightState.Text = DescribeBlock(_right.Block);
        UpdateBlockTile(_leftBlockTile, _left.Block, outgoingDirectionRight: true);
        UpdateBlockTile(_rightBlockTile, _right.Block, outgoingDirectionRight: false);
        _connectionState.Text =
            $"Zyklus: {_cycles} | Schleife verbunden: {_left.Block.m_schleife.IsValid()}";
    }

    private static void UpdateBlockTile(BlockTile tile, TMN840_BS block, bool outgoingDirectionRight)
    {
        tile.SetState(
            directionRight: block.F.Value ? outgoingDirectionRight : !outgoingDirectionRight,
            directionVisible: true,
            preBlocked: block.VB.Value || block.VE.Value,
            blocked: block.B.Value || block.BE.Value,
            locked: block.SP.Value || block.SPE.Value,
            releaseRequested: block.SAK.Value || block.SAE.Value);
    }

    private static string DescribeBlock(TMN840_BS block)
    {
        var text = new StringBuilder();
        text.AppendLine("MODELLBAHN-BEDIENRELAIS");
        AppendRelay(text, "B_FA", block.B_FA);
        AppendRelay(text, "B_VB", block.B_VB);
        AppendRelay(text, "B_RB", block.B_RB);
        AppendRelay(text, "B_BL", block.B_BL);
        AppendRelay(text, "B_FBH", block.B_FBH);
        AppendRelay(text, "B_FBF", block.B_FBF);
        AppendRelay(text, "B_SE", block.B_SE);
        AppendRelay(text, "B_SA", block.B_SA);
        AppendRelay(text, "FBH", block.FBH);
        text.AppendLine();
        text.AppendLine("BLOCKRELAIS");
        AppendRelay(text, "F", block.F);
        AppendRelay(text, "VB", block.VB);
        AppendRelay(text, "B", block.B);
        AppendRelay(text, "AF", block.AF);
        AppendRelay(text, "EF", block.EF);
        AppendRelay(text, "VE", block.VE);
        AppendRelay(text, "BE", block.BE);
        AppendRelay(text, "RB", block.RB);
        AppendRelay(text, "GF", block.GF);
        AppendRelay(text, "RBS", block.RBS);
        AppendRelay(text, "FBH", block.FBH);
        text.AppendLine();
        text.AppendLine("SPERRSATZRELAIS");
        AppendRelay(text, "SP", block.SP);
        AppendRelay(text, "SPE", block.SPE);
        AppendRelay(text, "SAK", block.SAK);
        AppendRelay(text, "SAE", block.SAE);
        AppendRelay(text, "SPA", block.SPA);
        text.AppendLine();
        text.AppendLine($"Blockschleife Spannung: {block.m_schleife.GetSpannung()}");
        text.AppendLine($"Blockschleife Minus:    {block.m_schleife.GetLeitwertMinus()}");
        text.AppendLine($"Blockschleife Plus:     {block.m_schleife.GetLeitwertPlus()}");
        text.AppendLine($"Sperrschleife Spannung: {block.m_sperrschleife.GetSpannung()}");
        text.AppendLine($"Sperrschleife Minus:    {block.m_sperrschleife.GetLeitwertMinus()}");
        text.AppendLine($"Sperrschleife Plus:     {block.m_sperrschleife.GetLeitwertPlus()}");
        return text.ToString();
    }

    private static void AppendRelay(StringBuilder text, string name, Relais relay)
    {
        text.AppendLine($"{name,-8} Kontakt {State(relay.Value),-3}  Spule {State(relay.sp)}");
    }

    private static string State(bool value) => value ? "EIN" : "aus";

    private void RunOneStep(object? sender, RoutedEventArgs e) => RunSteps(OperationCycles);

    private void ResetSimulation(object? sender, RoutedEventArgs e) => CreateSimulation();
}
