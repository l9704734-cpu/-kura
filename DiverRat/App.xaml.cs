using System.Configuration;
using System.Data;
using System.Windows;

namespace Diver_RaT
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var notice = new EducationalNoticeWindow();
            notice.ShowDialog();

            var endpoint = new EndpointSetupWindow();
            endpoint.ShowDialog();

            var mainWindow = new MainWindow();
            mainWindow.Closed += (_, _) => Shutdown();
            mainWindow.Show();
        }
    }

}
