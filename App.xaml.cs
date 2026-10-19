namespace PythonRunnerApp
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new AppShell();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = base.CreateWindow(activationState);

            window.Title = "Portable Python Runner";

#if WINDOWS
            window.Width = 1000;
            window.Height = 700;
            window.MinimumWidth = 800;
            window.MinimumHeight = 600;
#endif

            window.Destroying += Window_Destroying;

            return window;
        }

        private void Window_Destroying(object? sender, EventArgs e)
        {
            try
            {
                if (Windows.Count > 1)
                {
                    return;
                }

                int cleanupPolicy = Microsoft.Maui.Storage.Preferences.Get("CleanupPolicy", 0);
                string defaultPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython");
                string installLocation = Microsoft.Maui.Storage.Preferences.Get("PythonInstallLocation", defaultPath);

                if (cleanupPolicy == 1)
                {
                    string sitePackages = System.IO.Path.Combine(installLocation, "python", "Lib", "site-packages");
                    if (System.IO.Directory.Exists(sitePackages))
                    {
                        System.IO.Directory.Delete(sitePackages, true);
                        System.IO.Directory.CreateDirectory(sitePackages);
                    }
                }
                else if (cleanupPolicy == 2)
                {
                    if (System.IO.Directory.Exists(installLocation))
                    {
                        System.IO.Directory.Delete(installLocation, true);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
