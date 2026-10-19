using System;
using Avalonia;

namespace Center;

internal static class Program
{
    [STAThread] // Windows: OLE / clipboard / drag & drop need an STA main thread.
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
