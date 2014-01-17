using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace DmxPad
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            startArgs = e;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DmxPad.Properties.Settings.Default.Save();
            base.OnExit(e);
        }

        public static StartupEventArgs StartArgs { get { return startArgs; } }
        public static StartupEventArgs startArgs;

        public static RoutedCommand ChooseElement = new RoutedCommand();
    }
}
