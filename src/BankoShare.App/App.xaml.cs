using System.Windows;
namespace BankoShare.App;
public partial class App : Application
{
    /// <summary>Set by a second launch: the running instance shows its window instead.</summary>
    internal static EventWaitHandle? ShowSignal { get; private set; }
    /// <summary>Started by Windows at sign-in: stay in the notification area.</summary>
    internal static bool StartInTray { get; private set; }

    [STAThread]
    public static void Main()
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var testPort = Environment.GetEnvironmentVariable("BANKOSHARE_TEST_PORT");
        var name = "Local\\BankoShare.App" + (string.IsNullOrEmpty(testPort) ? "" : ".Test" + testPort);
        // One instance per user: parallel sessions would fight over the port and the updater over the installation.
        using var singleInstance = new Mutex(true, name, out var created);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
        if (!created) { showSignal.Set(); return; }
        ShowSignal = showSignal;
        StartInTray = Environment.GetCommandLineArgs().Skip(1).Contains(Autostart.TrayArgument, StringComparer.OrdinalIgnoreCase);
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    /// <summary>The window always exists; it starts hidden when Windows launched us at sign-in.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        if (!StartInTray) window.Show();
    }
}
