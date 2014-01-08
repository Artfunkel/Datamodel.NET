using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Datamodel;

namespace DmxPad
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<Datamodel.Datamodel> Datamodels = new ObservableCollection<Datamodel.Datamodel>();

        public MainWindow()
        {
            InitializeComponent();

            DataContext = Datamodels;
            if (Properties.Settings.Default.Recent == null)
                Properties.Settings.Default.Recent = new System.Collections.Specialized.StringCollection();

            try
            {
                Load(App.StartArgs.Args);
            }
            catch (Exception err)
            {
                System.Windows.MessageBox.Show(err.Message, "Startup argument error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region CommandBindings
        private void New_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var new_dm = new Datamodel.Datamodel("my_format", 1);
            Datamodels.Add(new_dm);
            Tabs.SelectedItem = new_dm;
            e.Handled = true;
        }
        private void OpenRecentItem(object sender, RoutedEventArgs e)
        {
            Load((sender as MenuItem).DataContext as string);
        }

        private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Multiselect = true;
            if (ofd.ShowDialog() == true)
            {
                Cursor = Cursors.Wait;
                try
                {
                    Load(ofd.FileNames);
                }
#if !DEBUG
                catch (Exception err)
                {
                    System.Windows.MessageBox.Show(err.Message, "DMX load error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
#endif
                finally
                {
                    Cursor = Cursors.Arrow;
                }
            }
            e.Handled = true;
        }

        public void Load(params string[] paths)
        {
            Datamodel.Datamodel new_dm = null;
            foreach (var path in paths)
            {
                new_dm = Datamodel.Datamodel.Load(path);
                Datamodels.Add(new_dm);
                Properties.Settings.Default.Recent.Remove(path);
                Properties.Settings.Default.Recent.Insert(0,path);
            }
            Tabs.SelectedItem = new_dm;
            RecentMenu.Items.Refresh();
        }

        private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var dm = Tabs.SelectedItem as Datamodel.Datamodel;
            if (String.IsNullOrEmpty(dm.FilePath))
            {
                SaveAs_Executed(sender, e);
            }
            else
                dm.Save(dm.FilePath,"binary",5);
        }

        private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog();
            var dm = Tabs.SelectedItem as Datamodel.Datamodel;

            sfd.InitialDirectory = dm.FilePath;
            sfd.FileName = dm.FilePath;
            sfd.Filter = "Datamodel Exchange (*.dmx)|*.dmx|All files (*.*)|*.*";
            if (sfd.ShowDialog() == true)
            {
                Cursor = Cursors.Wait;
                try
                {
                    dm.Save(sfd.FileName, "binary", 5);
                }
                catch (Exception err)
                {
                    System.Windows.MessageBox.Show(err.Message, "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Cursor = Cursors.Arrow;
                }
            }
            e.Handled = true;
        }

        private void Close_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var dm = ((e.OriginalSource as FrameworkElement).DataContext ?? Tabs.SelectedItem) as Datamodel.Datamodel;
            Datamodels.Remove(dm);
            dm.Dispose();
            e.Handled = true;
        }

        private void FileOperations_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Tabs.SelectedItem is Datamodel.Datamodel;
            e.Handled = true;
        }
        #endregion

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class DesignTimeData : ObservableCollection<Datamodel.Datamodel>
    {
        public DesignTimeData()
        {
            var dm = new Datamodel.Datamodel("design_data", 1);
            Add(dm);
            dm.Root = dm.CreateElement("root");
            dm.Root["BlankElem"] = null;
            dm.Root["StubElem"] = dm.CreateStubElement(Guid.NewGuid());
            dm.Root["Str"] = "Hello World";
            dm.Root["Vector"] = new Datamodel.Vector3(0, 0, 1.5f);
        }
    }
}
