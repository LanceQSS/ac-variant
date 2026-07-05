using System.Windows;
using Wpf.Ui.Appearance;

namespace Acvc.Gui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The dictionaries in App.xaml style the controls; this call actually paints
        // the application dark (without it, windows render on default white).
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
    }
}
