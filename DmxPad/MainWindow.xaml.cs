using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    public class ViewModel : INotifyPropertyChanged, IDisposable
    {
        public ViewModel(Datamodel.Datamodel datamodel)
        {
            Datamodel = datamodel;
        }

        public Datamodel.Datamodel Datamodel
        {
            get { return _Datamodel; }
            protected set { _Datamodel = value; NotifyPropertyChanged("Datamodel"); }
        }
        Datamodel.Datamodel _Datamodel;

        public Element DisplayRoot
        {
            get { return _DisplayRoot ?? Datamodel.Root; }
            set { _DisplayRoot = value; }
        }
        Element _DisplayRoot;

        public string Path
        {
            get { return _Path; }
            set { _Path = value; NotifyPropertyChanged("Path"); }
        }
        string _Path;

        public ComparisonDatamodel ComparisonDatamodel
        {
            get { return _ComparisonDatamodel; }
            set { _ComparisonDatamodel = value; NotifyPropertyChanged("ComparisonDatamodel"); }
        }
        ComparisonDatamodel _ComparisonDatamodel;

        public bool FilterComparison
        {
            get { return _FilterComparison; }
            set { _FilterComparison = value; NotifyPropertyChanged("FilterComparison"); }
        }
        bool _FilterComparison = false;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }

        public void Dispose()
        {
            if (Datamodel != null) Datamodel.Dispose();
            if (ComparisonDatamodel != null) ComparisonDatamodel.Datamodel_Right.Dispose();
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<ViewModel> Datamodels = new ObservableCollection<ViewModel>();

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
            var dm = new Datamodel.Datamodel("my_format", 1);
            dm.Root = dm.CreateElement("root");

            var vm = new ViewModel(dm);
            Datamodels.Add(vm);
            Tabs.SelectedItem = vm;
            
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
            var recent = Properties.Settings.Default.Recent;
            ViewModel new_dm = null;
            foreach (var path in paths)
            {
                new_dm = new ViewModel(Datamodel.Datamodel.Load(path));
                Datamodels.Add(new_dm);
                recent.Remove(path);
                recent.Insert(0, path);
            }
            while (recent.Count > 10)
                recent.RemoveAt(9);
            Tabs.SelectedItem = new_dm;
            RecentMenu.Items.Refresh();
        }

        private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var dm = Tabs.SelectedItem as Datamodel.Datamodel;
            if (String.IsNullOrEmpty(dm.File.FullName))
            {
                SaveAs_Executed(sender, e);
            }
            else
                dm.Save(dm.File.FullName, dm.Encoding, dm.EncodingVersion);
        }

        private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog();
            var dm = Tabs.SelectedItem as Datamodel.Datamodel;

            sfd.InitialDirectory = dm.File.DirectoryName;
            sfd.FileName = dm.File.Name;
            sfd.Filter = "Datamodel Exchange (*.dmx)|*.dmx|All files (*.*)|*.*";
            if (sfd.ShowDialog() == true)
            {
                Cursor = Cursors.Wait;
                try
                {
                    dm.Save(sfd.FileName, dm.Encoding, dm.EncodingVersion);
                    dm.File = new System.IO.FileInfo(sfd.FileName);
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
            var dm = (ViewModel)(((FrameworkElement)e.OriginalSource).DataContext ?? Tabs.SelectedItem);
            Datamodels.Remove(dm);
            dm.Dispose();
            e.Handled = true;
        }

        private void FileOperations_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Tabs.SelectedItem is ViewModel;
            e.Handled = true;
        }
        #endregion

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RegisterExtensions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var class_name = "DmxPadFile";
                using (var classes = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes", true))
                {
                    var path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var file_def = classes.CreateSubKey(class_name);
                    file_def.SetValue(null, "Datamodel Exchange File");
                    file_def.CreateSubKey(@"shell\edit\command").SetValue(null, String.Format("{0} \"%1\"", path));
                    file_def.CreateSubKey("DefaultIcon").SetValue(null, String.Format("{0},0", path));

                    classes.CreateSubKey(".dmx").SetValue(null, class_name);
                }
                System.Windows.MessageBox.Show("File extensions registered for current user.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception err)
            {
                System.Windows.MessageBox.Show(err.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CompareDatamodel_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog();
            var vm = (ViewModel)Tabs.SelectedItem;
            
            var current_file = vm.Datamodel.File;
            if (current_file != null)
                ofd.InitialDirectory = current_file.DirectoryName;

            if (ofd.ShowDialog() == true)
            {
                Cursor = Cursors.Wait;
                try
                {
                    vm.ComparisonDatamodel = new ComparisonDatamodel(vm.Datamodel, Datamodel.Datamodel.Load(ofd.FileName));
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
    }

    class DesignTimeData : ObservableCollection<ViewModel>
    {
        public DesignTimeData()
        {
            var dm = new Datamodel.Datamodel("design_data", 1);
            var vm = new ViewModel(dm);
            Add(vm);

            dm.Root = dm.CreateElement("root");
            dm.Root["BlankElem"] = null;
            dm.Root["StubElem"] = dm.CreateStubElement(Guid.NewGuid());
            dm.Root["Str"] = "Hello World";
            dm.Root["Vector"] = new Datamodel.Vector3(0, 0, 1.5f);

            vm.Path = "//root/str";
        }
    }
}
