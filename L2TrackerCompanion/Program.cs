using Velopack;

namespace L2TrackerCompanion;

public static class Program
{
    // Velopack intercepts its own install/uninstall/update lifecycle command-line
    // args, so VelopackApp.Build().Run() has to be the very first thing that runs —
    // before the WPF Application (and its MainWindow) is ever constructed. That's
    // why this project no longer starts via App.xaml's StartupUri.
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
