using System;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace OTD;

public partial class WsrTestWindow : Window
{
    private readonly CheckBox _isFreeCheckBox;
    private readonly CheckBox _wsrButtonCheckBox;
    private readonly CheckBox _remoteWsrButtonCheckBox;
    private readonly CheckBox _enableWsrButtonCheckBox;
    private readonly CheckBox _evOnCheckBox;
    private readonly CheckBox _evOffCheckBox;
    private readonly CheckBox _amPermissionCheckBox;
    private readonly CheckBox _stPermissionCheckBox;
    private readonly CheckBox _wiuPermissionCheckBox;
    private readonly CheckBox _indicatorCheckBox;
    private readonly CheckBox _forceWvCheckBox;
    private readonly TextBlock _stateText;
    private readonly TextBlock _cycleText;
    private readonly TextBlock _switchPositionText;
    private readonly Ellipse _wvLamp;
    private readonly Ellipse _tipWhiteLamp;
    private readonly Ellipse _tipRedLamp;
    private readonly Ellipse _leftWhiteLamp;
    private readonly Ellipse _leftRedLamp;
    private readonly Ellipse _rightWhiteLamp;
    private readonly Ellipse _rightRedLamp;

    private Do67 _do67 = null!;
    private TMN500_WSR _wsr = null!;
    private Weichenantrieb _switch = null!;
    private Stellstrom _switchPower;
    private int _cycles;
    private bool _isInitialized;

    public WsrTestWindow()
    {
        InitializeComponent();

        _isFreeCheckBox = Find<CheckBox>("IsFreeCheckBox");
        _wsrButtonCheckBox = Find<CheckBox>("WsrButtonCheckBox");
        _remoteWsrButtonCheckBox = Find<CheckBox>("RemoteWsrButtonCheckBox");
        _enableWsrButtonCheckBox = Find<CheckBox>("EnableWsrButtonCheckBox");
        _evOnCheckBox = Find<CheckBox>("EvOnCheckBox");
        _evOffCheckBox = Find<CheckBox>("EvOffCheckBox");
        _amPermissionCheckBox = Find<CheckBox>("AmPermissionCheckBox");
        _stPermissionCheckBox = Find<CheckBox>("StPermissionCheckBox");
        _wiuPermissionCheckBox = Find<CheckBox>("WiuPermissionCheckBox");
        _indicatorCheckBox = Find<CheckBox>("IndicatorCheckBox");
        _forceWvCheckBox = Find<CheckBox>("ForceWvCheckBox");
        _stateText = Find<TextBlock>("StateText");
        _cycleText = Find<TextBlock>("CycleText");
        _switchPositionText = Find<TextBlock>("SwitchPositionText");
        _wvLamp = Find<Ellipse>("WvLamp");
        _tipWhiteLamp = Find<Ellipse>("TipWhiteLamp");
        _tipRedLamp = Find<Ellipse>("TipRedLamp");
        _leftWhiteLamp = Find<Ellipse>("LeftWhiteLamp");
        _leftRedLamp = Find<Ellipse>("LeftRedLamp");
        _rightWhiteLamp = Find<Ellipse>("RightWhiteLamp");
        _rightRedLamp = Find<Ellipse>("RightRedLamp");

        CreateTestObject();
        _isInitialized = true;
        RunSteps(2);
        ShowState();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private T Find<T>(string name) where T : Control
    {
        return this.FindControl<T>(name)
               ?? throw new InvalidOperationException($"Control '{name}' wurde nicht gefunden.");
    }

    private void CreateTestObject()
    {
        _do67 = new Do67();
        _wsr = new TMN500_WSR(_do67, spitze_rechts: false);
        _switch = new Weichenantrieb();
        _switch.Init(20, plus_ist_links: false);
        _wsr.SetWeiche(_switch);
        _switchPower = Stellstrom.Aus;
        _cycles = 0;
    }

    private void ApplyInputs()
    {
        _wsr.i_IS.Value = IsChecked(_isFreeCheckBox);
        _wsr.t_WT.Value = IsChecked(_wsrButtonCheckBox);
        _wsr.tfu_WT.Value = IsChecked(_remoteWsrButtonCheckBox);
        _do67.sl_TR_W.Value = IsChecked(_enableWsrButtonCheckBox);
        _do67.sl_EV_E.Value = IsChecked(_evOnCheckBox);
        _do67.sl_EV_A.Value = IsChecked(_evOffCheckBox);
        _do67.sl_AM_GT.Value = IsChecked(_amPermissionCheckBox);
        _do67.sl_ST_GT.Value = IsChecked(_stPermissionCheckBox);
        _do67.sl_WIU_GT.Value = IsChecked(_wiuPermissionCheckBox);
        _do67.sl_ML.Value = IsChecked(_indicatorCheckBox);
    }

    private void RunSteps(int count)
    {
        ApplyInputs();

        for (var i = 0; i < count; i++)
        {
            _wsr.Update();
            UpdatePrivateCapacitors();
            CommitRelays();
            _wsr.UpdateWire();
            ApplyTestOverrides();
            _wsr.Output();
            _cycles++;
        }

        ShowState();
    }

    private void CommitRelays()
    {
        foreach (var field in typeof(TMN500_WSR).GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            switch (field.GetValue(_wsr))
            {
                case Relais relay:
                    relay.Commit();
                    break;
                case RelaisInv inverseRelay:
                    inverseRelay.Commit();
                    break;
                case Flachrelais flatRelay:
                    flatRelay.Commit();
                    break;
            }
        }
    }

    private void UpdatePrivateCapacitors()
    {
        foreach (var field in typeof(TMN500_WSR).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (field.GetValue(_wsr) is Kondensator capacitor)
            {
                capacitor.Update();
            }
        }
    }

    private void ApplyTestOverrides()
    {
        _wsr.WV.Set(IsChecked(_forceWvCheckBox));
    }

    private void ShowState()
    {
        var text = new StringBuilder();
        text.AppendLine("RELAIS");
        AppendRelay(text, "AH", _wsr.AH);
        AppendRelay(text, "ARL", _wsr.ARL);
        AppendRelay(text, "SV", _wsr.SV);
        AppendRelay(text, "SUR", _wsr.SUR);
        AppendRelay(text, "SUL", _wsr.SUL);
        AppendRelay(text, "IS", _wsr.IS);
        AppendRelay(text, "U", _wsr.U);
        AppendRelay(text, "AM", _wsr.AM);
        AppendRelay(text, "WT", _wsr.WT);
        AppendRelay(text, "EV", _wsr.EV);
        AppendRelay(text, "WV", _wsr.WV);
        AppendRelay(text, "L1", _wsr.L1);
        AppendRelay(text, "L2", _wsr.L2);
        AppendRelay(text, "L3", _wsr.L3);
        AppendRelay(text, "L4", _wsr.L4);
        AppendRelay(text, "S1", _wsr.S1);
        AppendRelay(text, "S2", _wsr.S2);
        AppendRelay(text, "FAR", _wsr.FAR);
        AppendRelay(text, "FAL", _wsr.FAL);
        AppendRelay(text, "FUS", _wsr.FUS);
        AppendRelay(text, "FURL", _wsr.FURL);

        text.AppendLine();
        text.AppendLine("AUSGÄNGE");
        AppendValue(text, "o_Sp_S_S", _wsr.o_Sp_S_S.Value);
        AppendValue(text, "o_Sp_S_Q", _wsr.o_Sp_S_Q.Value);
        AppendValue(text, "o_23", _wsr.o_23.Value);

        text.AppendLine();
        text.AppendLine("LAMPEN");
        AppendValue(text, "WV", _wsr.l_WV.Value);
        AppendValue(text, "S weiß", _wsr.l_s_ws.Value);
        AppendValue(text, "S rot", _wsr.l_s_rt.Value);
        AppendValue(text, "L weiß", _wsr.l_l_ws.Value);
        AppendValue(text, "L rot", _wsr.l_l_rt.Value);
        AppendValue(text, "R weiß", _wsr.l_r_ws.Value);
        AppendValue(text, "R rot", _wsr.l_r_rt.Value);

        _stateText.Text = text.ToString();
        _cycleText.Text = $"Zyklus: {_cycles}";
        _switchPositionText.Text =
            $"Antrieb: {GetSwitchPosition()} / Relaislage: {GetRelayPosition()} / Strom: {_switchPower}";
        SetLamp(_wvLamp, _wsr.l_WV.Value, Colors.Yellow);
        SetLamp(_tipWhiteLamp, _wsr.l_s_ws.Value, Colors.White);
        SetLamp(_tipRedLamp, _wsr.l_s_rt.Value, Colors.Red);
        SetLamp(_leftWhiteLamp, _wsr.l_l_ws.Value, Colors.White);
        SetLamp(_leftRedLamp, _wsr.l_l_rt.Value, Colors.Red);
        SetLamp(_rightWhiteLamp, _wsr.l_r_ws.Value, Colors.White);
        SetLamp(_rightRedLamp, _wsr.l_r_rt.Value, Colors.Red);
    }

    private string GetSwitchPosition()
    {
        if (_switch.elk_l()) return "links";
        if (_switch.elk_r()) return "rechts";
        return "in Bewegung";
    }

    private string GetRelayPosition()
    {
        if (!_wsr.L1.Value && !_wsr.L2.Value && !_wsr.L3.Value && !_wsr.L4.Value) return "rechts";
        if (_wsr.L1.Value && _wsr.L2.Value && _wsr.L3.Value && _wsr.L4.Value) return "links";
        return "uneindeutig";
    }

    private static void AppendRelay(StringBuilder text, string name, RelaisBase relay)
    {
        text.AppendLine($"{name,-8} {State(relay.Value)}");
    }

    private static void AppendValue(StringBuilder text, string name, bool value)
    {
        text.AppendLine($"{name,-12} {State(value)}");
    }

    private static string State(bool value) => value ? "EIN" : "aus";

    private static bool IsChecked(CheckBox checkBox) => checkBox.IsChecked == true;

    private static void SetLamp(Shape lamp, bool isOn, Color color)
    {
        lamp.Fill = new SolidColorBrush(isOn ? color : Color.Parse("#303030"));
    }

    private void InputChanged(object? sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            RunSteps(2);
        }
    }

    private void RunOneStep(object? sender, RoutedEventArgs e) => RunSteps(1);

    private void RunTenSteps(object? sender, RoutedEventArgs e) => RunSteps(10);

    private void MoveSwitchLeft(object? sender, RoutedEventArgs e)
    {
        MoveSwitchTo(Stellstrom.Links);
    }

    private void StopSwitch(object? sender, RoutedEventArgs e)
    {
        _switchPower = Stellstrom.Aus;
        _switch.SetStrom(Stellstrom.Aus);
        SynchronizeDisplayedSwitchPosition();
        _wsr.Output();
        ShowState();
    }

    private void MoveSwitchRight(object? sender, RoutedEventArgs e)
    {
        MoveSwitchTo(Stellstrom.Rechts);
    }

    private void MoveSwitchTo(Stellstrom direction)
    {
        _switchPower = direction;
        _switch.SetStrom(direction);

        for (var i = 0; i < 100 && !HasReachedEndPosition(direction); i++)
        {
            _switch.Commit();
        }

        _switchPower = Stellstrom.Aus;
        _switch.SetStrom(Stellstrom.Aus);
        SynchronizeDisplayedSwitchPosition();
        _wsr.Output();
        ShowState();
    }

    private bool HasReachedEndPosition(Stellstrom direction)
    {
        return direction == Stellstrom.Links ? _switch.elk_l() : _switch.elk_r();
    }

    private void SynchronizeDisplayedSwitchPosition()
    {
        var isLeft = _switch.elk_l();
        _wsr.L1.Set(isLeft);
        _wsr.L2.Set(isLeft);
        _wsr.L3.Set(isLeft);
        _wsr.L4.Set(isLeft);
        ApplyTestOverrides();
    }

    private void ResetTest(object? sender, RoutedEventArgs e)
    {
        CreateTestObject();
        RunSteps(2);
    }
}
