using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OTD.UI.ViewModels;

public partial class SettingsWindow : Window
{
    private const string SettingsFileName = "settings.xml";

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void SaveClicked(object? sender, RoutedEventArgs e)
    {
        if (!ValidateSettings(out var error))
        {
            SetStatus(error);
            return;
        }

        SaveSettings();
        SetStatus("Einstellungen gespeichert.");
        Close(true);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private bool ValidateSettings(out string error)
    {
        var host = GetText("HostBox");
        var project = GetText("ProjectNameBox");
        var logPath = GetText("LogPathBox");

        if (string.IsNullOrWhiteSpace(project))
        {
            error = "Projektname ist erforderlich.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "Host der Zentrale ist erforderlich.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(logPath))
        {
            error = "Log-Pfad ist erforderlich.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void LoadSettings()
    {
        var filePath = GetDataFilePath(SettingsFileName);
        if (!File.Exists(filePath))
        {
            ApplyDefaults();
            return;
        }

        try
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root is null)
            {
                ApplyDefaults();
                return;
            }

            var general = root.Element("general");
            var connection = root.Element("connection");
            var operation = root.Element("operation");
            var automation = root.Element("automation");
            var feedback = root.Element("feedback");
            var safety = root.Element("safety");
            var editor = root.Element("editor");
            var ui = root.Element("ui");
            var logging = root.Element("logging");

            SetText("ProjectNameBox", Attr(general, "project", "OpenTrainDrive"));
            SetCombo("LanguageBox", Attr(general, "language", "de"));
            SetCombo("ProfileBox", Attr(general, "profile", "single-user"));
            SetCheck("AutosaveBox", AttrBool(general, "autosave", true));
            SetNumeric("AutosaveIntervalBox", AttrInt(general, "autosaveInterval", 120));
            SetCheck("AutoOpenBox", AttrBool(general, "autoOpen", true));

            SetCombo("SystemBox", Attr(connection, "system", "dcc"));
            SetText("HostBox", Attr(connection, "host", "localhost"));
            SetNumeric("PortBox", AttrInt(connection, "port", 5550));
            SetCombo("BaudBox", Attr(connection, "baud", "115200"));
            SetNumeric("HeartbeatBox", AttrInt(connection, "heartbeat", 15));
            SetNumeric("RetryCountBox", AttrInt(connection, "retryCount", 3));
            SetCheck("AutoconnectBox", AttrBool(connection, "autoconnect", true));

            SetCombo("AutomationModeBox", Attr(automation, "mode", "manual"));
            SetNumeric("MaxSpeedBox", AttrInt(operation, "maxSpeed", 120));
            SetNumeric("AccelFactorBox", AttrInt(operation, "accelFactor", 1));
            SetNumeric("BrakeFactorBox", AttrInt(operation, "brakeFactor", 1));
            SetNumeric("ShuntingSpeedBox", AttrInt(operation, "shuntingSpeed", 40));
            SetNumeric("BlockWaitSecondsBox", AttrInt(automation, "blockWaitSeconds", 5));
            SetCheck("StopOnSignalBox", AttrBool(operation, "stopOnSignal", true));
            SetCheck("RouteReservationBox", AttrBool(automation, "routeReservation", true));
            SetCheck("DeadlockDetectionBox", AttrBool(automation, "deadlockDetection", true));

            SetCombo("FeedbackBusBox", Attr(feedback, "bus", "s88"));
            SetNumeric("PollingMsBox", AttrInt(feedback, "pollingMs", 150));
            SetNumeric("DebounceMsBox", AttrInt(feedback, "debounceMs", 60));
            SetNumeric("OccupancyTimeoutBox", AttrInt(feedback, "occupancyTimeout", 20));
            SetNumeric("WatchdogSecondsBox", AttrInt(safety, "watchdogSeconds", 8));
            SetCheck("InvertFeedbackLogicBox", AttrBool(feedback, "invertLogic", false));
            SetCheck("EmergencyStopBox", AttrBool(safety, "emergencyStop", true));
            SetCheck("PowerOnAtStartBox", AttrBool(safety, "powerOnAtStart", false));

            SetCombo("ThemeBox", Attr(ui, "theme", "classic"));
            SetCombo("DensityBox", Attr(ui, "density", "compact"));
            SetNumeric("GridSizeBox", AttrInt(editor, "grid", 48));
            SetNumeric("SymbolSizeBox", AttrInt(editor, "symbolSize", 32));
            SetCombo("LogLevelBox", Attr(logging, "level", "info"));
            SetNumeric("LogKeepDaysBox", AttrInt(logging, "keepDays", 7));
            SetText("LogPathBox", Attr(logging, "path", "logs"));
            SetCheck("ShowTooltipsBox", AttrBool(ui, "tooltips", true));
            SetCheck("ShowClockBox", AttrBool(ui, "clock", false));
            SetCheck("ShowStatusBarBox", AttrBool(ui, "statusbar", true));
        }
        catch
        {
            ApplyDefaults();
        }
    }

    private void SaveSettings()
    {
        var root = new XElement("settings",
            new XElement("general",
                new XAttribute("project", GetText("ProjectNameBox")),
                new XAttribute("language", GetCombo("LanguageBox", "de")),
                new XAttribute("profile", GetCombo("ProfileBox", "single-user")),
                new XAttribute("autosave", GetCheck("AutosaveBox")),
                new XAttribute("autosaveInterval", GetInt("AutosaveIntervalBox", 120)),
                new XAttribute("autoOpen", GetCheck("AutoOpenBox")),
                new XAttribute("usersEnabled", false)),

            new XElement("connection",
                new XAttribute("system", GetCombo("SystemBox", "dcc")),
                new XAttribute("host", GetText("HostBox")),
                new XAttribute("port", GetInt("PortBox", 5550)),
                new XAttribute("baud", GetCombo("BaudBox", "115200")),
                new XAttribute("autoconnect", GetCheck("AutoconnectBox")),
                new XAttribute("heartbeat", GetInt("HeartbeatBox", 15)),
                new XAttribute("retryCount", GetInt("RetryCountBox", 3))),

            new XElement("automation",
                new XAttribute("mode", GetCombo("AutomationModeBox", "manual")),
                new XAttribute("routeReservation", GetCheck("RouteReservationBox")),
                new XAttribute("deadlockDetection", GetCheck("DeadlockDetectionBox")),
                new XAttribute("scheduleEnabled", true),
                new XAttribute("blockWaitSeconds", GetInt("BlockWaitSecondsBox", 5))),

            new XElement("operation",
                new XAttribute("maxSpeed", GetInt("MaxSpeedBox", 120)),
                new XAttribute("accelFactor", GetInt("AccelFactorBox", 1)),
                new XAttribute("brakeFactor", GetInt("BrakeFactorBox", 1)),
                new XAttribute("stopOnSignal", GetCheck("StopOnSignalBox")),
                new XAttribute("cruiseControl", true),
                new XAttribute("shuntingSpeed", GetInt("ShuntingSpeedBox", 40))),

            new XElement("feedback",
                new XAttribute("bus", GetCombo("FeedbackBusBox", "s88")),
                new XAttribute("pollingMs", GetInt("PollingMsBox", 150)),
                new XAttribute("debounceMs", GetInt("DebounceMsBox", 60)),
                new XAttribute("invertLogic", GetCheck("InvertFeedbackLogicBox")),
                new XAttribute("occupancyTimeout", GetInt("OccupancyTimeoutBox", 20))),

            new XElement("safety",
                new XAttribute("emergencyStop", GetCheck("EmergencyStopBox")),
                new XAttribute("shortCircuitCutoff", true),
                new XAttribute("watchdogSeconds", GetInt("WatchdogSecondsBox", 8)),
                new XAttribute("powerOnAtStart", GetCheck("PowerOnAtStartBox"))),

            new XElement("editor",
                new XAttribute("grid", GetInt("GridSizeBox", 48)),
                new XAttribute("snap", true),
                new XAttribute("showLabels", true),
                new XAttribute("showIds", false),
                new XAttribute("confirmDelete", true),
                new XAttribute("symbolSize", GetInt("SymbolSizeBox", 32))),

            new XElement("ui",
                new XAttribute("theme", GetCombo("ThemeBox", "classic")),
                new XAttribute("density", GetCombo("DensityBox", "compact")),
                new XAttribute("tooltips", GetCheck("ShowTooltipsBox")),
                new XAttribute("clock", GetCheck("ShowClockBox")),
                new XAttribute("statusbar", GetCheck("ShowStatusBarBox")),
                new XAttribute("expertMode", false)),

            new XElement("logging",
                new XAttribute("level", GetCombo("LogLevelBox", "info")),
                new XAttribute("keepDays", GetInt("LogKeepDaysBox", 7)),
                new XAttribute("path", GetText("LogPathBox")),
                new XAttribute("traceProtocol", false),
                new XAttribute("traceCommands", false))
        );

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        doc.Save(GetDataFilePath(SettingsFileName));
    }

    private void ApplyDefaults()
    {
        SetText("ProjectNameBox", "OpenTrainDrive");
        SetCombo("LanguageBox", "de");
        SetCombo("ProfileBox", "single-user");
        SetCheck("AutosaveBox", true);
        SetNumeric("AutosaveIntervalBox", 120);
        SetCheck("AutoOpenBox", true);

        SetCombo("SystemBox", "dcc");
        SetText("HostBox", "localhost");
        SetNumeric("PortBox", 5550);
        SetCombo("BaudBox", "115200");
        SetNumeric("HeartbeatBox", 15);
        SetNumeric("RetryCountBox", 3);
        SetCheck("AutoconnectBox", true);

        SetCombo("AutomationModeBox", "manual");
        SetNumeric("MaxSpeedBox", 120);
        SetNumeric("AccelFactorBox", 1);
        SetNumeric("BrakeFactorBox", 1);
        SetNumeric("ShuntingSpeedBox", 40);
        SetNumeric("BlockWaitSecondsBox", 5);
        SetCheck("StopOnSignalBox", true);
        SetCheck("RouteReservationBox", true);
        SetCheck("DeadlockDetectionBox", true);

        SetCombo("FeedbackBusBox", "s88");
        SetNumeric("PollingMsBox", 150);
        SetNumeric("DebounceMsBox", 60);
        SetNumeric("OccupancyTimeoutBox", 20);
        SetNumeric("WatchdogSecondsBox", 8);
        SetCheck("InvertFeedbackLogicBox", false);
        SetCheck("EmergencyStopBox", true);
        SetCheck("PowerOnAtStartBox", false);

        SetCombo("ThemeBox", "classic");
        SetCombo("DensityBox", "compact");
        SetNumeric("GridSizeBox", 48);
        SetNumeric("SymbolSizeBox", 32);
        SetCombo("LogLevelBox", "info");
        SetNumeric("LogKeepDaysBox", 7);
        SetText("LogPathBox", "logs");
        SetCheck("ShowTooltipsBox", true);
        SetCheck("ShowClockBox", false);
        SetCheck("ShowStatusBarBox", true);
    }

    private static string Attr(XElement? node, string name, string fallback)
    {
        return (string?)node?.Attribute(name) ?? fallback;
    }

    private static bool AttrBool(XElement? node, string name, bool fallback)
    {
        return bool.TryParse((string?)node?.Attribute(name), out var value) ? value : fallback;
    }

    private static int AttrInt(XElement? node, string name, int fallback)
    {
        return int.TryParse((string?)node?.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private string GetText(string controlName)
    {
        return this.FindControl<TextBox>(controlName)?.Text?.Trim() ?? string.Empty;
    }

    private void SetText(string controlName, string value)
    {
        var box = this.FindControl<TextBox>(controlName);
        if (box is not null)
            box.Text = value;
    }

    private bool GetCheck(string controlName)
    {
        return this.FindControl<CheckBox>(controlName)?.IsChecked ?? false;
    }

    private void SetCheck(string controlName, bool value)
    {
        var box = this.FindControl<CheckBox>(controlName);
        if (box is not null)
            box.IsChecked = value;
    }

    private int GetInt(string controlName, int fallback)
    {
        return (int)(this.FindControl<NumericUpDown>(controlName)?.Value ?? fallback);
    }

    private void SetNumeric(string controlName, int value)
    {
        var box = this.FindControl<NumericUpDown>(controlName);
        if (box is not null)
            box.Value = value;
    }

    private string GetCombo(string controlName, string fallback)
    {
        var combo = this.FindControl<ComboBox>(controlName);
        if (combo?.SelectedItem is not null)
            return combo.SelectedItem.ToString() ?? fallback;

        return fallback;
    }

    private void SetCombo(string controlName, string value)
    {
        var combo = this.FindControl<ComboBox>(controlName);
        if (combo is null)
            return;

        var item = combo.Items?.Cast<object?>()
            .FirstOrDefault(x => string.Equals(x?.ToString(), value, StringComparison.OrdinalIgnoreCase));

        combo.SelectedItem = item ?? combo.Items?.Cast<object?>().FirstOrDefault();
    }

    private void SetStatus(string message)
    {
        var status = this.FindControl<TextBlock>("StatusText");
        if (status is not null)
            status.Text = message;
    }

    private static string GetDataFilePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.EnumerateFiles(directory.FullName, "*.csproj").Any())
                return Path.Combine(directory.FullName, fileName);

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, fileName);
    }
}
