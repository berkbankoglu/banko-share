using System.Windows;
namespace BankoShare.App;
public partial class App : Application
{
    [STAThread]
    public static void Main()
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        // Prevent parallel sessions and two updaters racing over the same installation.
        using var singleInstance = new System.Threading.Mutex(true, "Local\\BankoShare.App", out var created);
        if (!created) { MessageBox.Show("Banko Share zaten açık.", "Banko Share"); return; }
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
