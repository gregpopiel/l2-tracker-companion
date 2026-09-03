using System.Windows;

namespace L2TrackerCompanion;

public partial class App : Application
{
    // Velopack's VelopackApp.Build().Run() must run before the Application object
    // is constructed (see Program.cs), which means this can no longer start from
    // App.xaml's StartupUri — Program.Main creates App and calls Run() itself, so
    // OnStartup is what opens the window instead.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
