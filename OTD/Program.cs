using Avalonia;
using System;
using OTD.HardwareControl.Examples;

namespace OTD;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var entryPoint = Environment.GetEnvironmentVariable("OTD_ENTRYPOINT");
        var normalizedEntryPoint = string.IsNullOrWhiteSpace(entryPoint)
            ? null
            : entryPoint.Trim().ToUpperInvariant();

        switch (normalizedEntryPoint)
        {
            case "TEST_HARDWARECONTROL":
                TrainTest.Main(args).GetAwaiter().GetResult();
                return;

            default:
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                break;
        }
       
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
