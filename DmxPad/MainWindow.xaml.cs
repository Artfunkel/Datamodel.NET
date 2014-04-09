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
using System.IO;

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
            set { _Datamodel = value; NotifyPropertyChanged("Datamodel"); }
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

        public FileInfo File
        {
            get { return _File; }
            set { _File = value; NotifyPropertyChanged("File"); }
        }
        FileInfo _File;

        public FileInfo ComparisonFile
        {
            get { return _ComparisonFile; }
            set { _ComparisonFile = value; NotifyPropertyChanged("ComparisonFile"); }
        }
        FileInfo _ComparisonFile;

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

        public bool UseListView
        {
            get { return _UseListView; }
            set { _UseListView = value; NotifyPropertyChanged("UseListView"); }
        }
        bool _UseListView = false;

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
        }

        protected override void OnInitialized(EventArgs e)
        {
            Cursor = Cursors.Wait;
            try
            {
                Load_UI(App.StartArgs.Args);
            }
#if !DEBUG
            catch (Exception err)
            {
                System.Windows.MessageBox.Show(err.Message, "Startup argument error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
#endif
            finally
            {
                Cursor = Cursors.Arrow;
            }
            base.OnInitialized(e);
        }

        Datamodel.Datamodel Load(string path)
        {
            Cursor = Cursors.Wait;
            try
            {
                return Datamodel.Datamodel.Load(path);
            }
#if !DEBUG
            catch (Exception err)
            {
                System.Windows.MessageBox.Show(err.Message, "DMX load error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
#endif
            finally
            {
                Cursor = Cursors.Arrow;
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
            Load_UI((sender as MenuItem).DataContext as string);
        }

        private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Multiselect = true;
            if (ofd.ShowDialog() == true)
                Load_UI(ofd.FileNames);

            e.Handled = true;
        }

        public void Load_UI(params string[] paths)
        {
            if (Properties.Settings.Default.Recent == null) Properties.Settings.Default.Recent = new System.Collections.Specialized.StringCollection();
            var recent = Properties.Settings.Default.Recent;

            ViewModel new_dm = null;
            foreach (var path in paths)
            {
                new_dm = new ViewModel(Load(path)) { File = new FileInfo(path) };
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
            var vm = (ViewModel)e.Parameter;
            if (vm.File == null)
            {
                SaveAs_Executed(sender, e);
            }
            else
                vm.Datamodel.Save(vm.File.FullName, vm.Datamodel.Encoding, vm.Datamodel.EncodingVersion);
        }

        private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog();
            var vm = (ViewModel)e.Parameter;

            sfd.InitialDirectory = vm.File.Directory.FullName;
            sfd.FileName = vm.File.Name;
            sfd.Filter = "Datamodel Exchange (*.dmx)|*.dmx|All files (*.*)|*.*";
            if (sfd.ShowDialog() == true)
            {
                Cursor = Cursors.Wait;
                try
                {
                    vm.Datamodel.Save(sfd.FileName, vm.Datamodel.Encoding, vm.Datamodel.EncodingVersion);
                    vm.File = new System.IO.FileInfo(sfd.FileName);
                }
#if !DEBUG
                catch (Exception err)
                {
                    System.Windows.MessageBox.Show(err.Message, "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
#endif
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
#if DEBUG
                throw err;
#else
                System.Windows.MessageBox.Show(err.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
            }
        }

        private void CompareDatamodel_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var vm = (ViewModel)Tabs.SelectedItem;

            if (vm.ComparisonDatamodel != null)
            {
                vm.ComparisonDatamodel = null;
                return;
            }

            var ofd = new Microsoft.Win32.OpenFileDialog();
            
            var current_file = vm.File;
            if (current_file != null)
            {
                ofd.InitialDirectory = current_file.DirectoryName;
                ofd.FileName = current_file.Name;
            }

            if (ofd.ShowDialog() == true)
            {
                Cursor = Cursors.Wait;
                try
                {
                    vm.ComparisonDatamodel = new ComparisonDatamodel(vm.Datamodel, Datamodel.Datamodel.Load(ofd.FileName));
                    vm.ComparisonFile = new FileInfo(ofd.FileName);
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

        private void Refresh_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var vm = (ViewModel)Tabs.SelectedItem;

            var new_dm = Load(vm.File.FullName);
            if (new_dm != null)
            {
                vm.Datamodel = new_dm;
                var cdm = vm.ComparisonDatamodel;
                if (cdm != null)
                {
                    var new_dm_left = vm.File != vm.ComparisonFile ? new_dm : cdm.Datamodel_Left;
                    var new_dm_right = Load(vm.ComparisonFile.FullName) ?? cdm.Datamodel_Right;

                    if (new_dm_right != null)
                        vm.ComparisonDatamodel = new ComparisonDatamodel(new_dm_left, new_dm_right);
                }
            }
        }
    }

    class DesignTimeData : ObservableCollection<ViewModel>
    {
        public DesignTimeData()
        {
            var dm = new Datamodel.Datamodel("design_data", 1);
            var vm = new ViewModel(dm);
            vm.UseListView = true;
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
