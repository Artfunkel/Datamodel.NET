using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Interaction logic for DmxView.xaml
    /// </summary>
    public partial class DmxView : UserControl, INotifyPropertyChanged
    {
        public DmxView()
        {
            InitializeComponent();
        }

        public Datamodel.Datamodel Datamodel
        {
            get { return GetValue(DatamodelProperty) as Datamodel.Datamodel; }
            set { SetValue(DatamodelProperty, value); }
        }
        public static DependencyProperty DatamodelProperty = DependencyProperty.Register("Datamodel", typeof(Datamodel.Datamodel), typeof(DmxView), new PropertyMetadata(DatamodelChanged));

        private static void DatamodelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue != null)
            {
                (sender as DmxView).SetRoot((e.NewValue as Datamodel.Datamodel).Root);
            }
        }

        private static void ExpandNode(object sender, EventArgs e)
        {
            var generator = sender as ItemContainerGenerator;
            if (generator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
            {
                (generator.ContainerFromIndex(0) as TreeViewItem).IsExpanded = true;
                generator.StatusChanged -= ExpandNode;
            }
        }

        public bool HasUnsavedChanges { get { return hasUnsavedChanges; } private set { hasUnsavedChanges = value; NotifyPropertyChanged("HasUnsavedChanges"); } }
        bool hasUnsavedChanges;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }

        string DisplayRootPath = "";
        
        private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
        {
            SelectedPath = MakeSelectionPath(e.OriginalSource as TreeViewItem);
        }

        private string MakeSelectionPath(TreeViewItem control)
        {
            FrameworkElement current_control = control;
            List<string> path_components = new List<string>();
            object array_item = null;

            while (true)
            {
                if (current_control is TreeView) break;
                if ((current_control is TreeViewItem))
                {
                    var current_item = current_control.DataContext;
                    var attr = current_item as Datamodel.Attribute;

                    if (attr == null || attr.Value is Element || attr.Value is IEnumerable<Element>)
                    {
                        if (attr != null)
                        {
                            if (array_item == null)
                                path_components.Add(attr.Name);
                            else
                            {
                                int i = 0;
                                foreach (var item in attr.Value as System.Collections.IEnumerable)
                                {
                                    if (item == array_item)
                                    {
                                        path_components.Add(attr.Name + String.Format("[{0}]", i));
                                        i = -1;
                                        break;
                                    }
                                    i++;
                                }
                                if (i != -1) throw new IndexOutOfRangeException();
                            }
                            array_item = null;
                        }
                        else
                        {
                            array_item = current_item;
                        }
                    }
                }
                current_control = VisualTreeHelper.GetParent(current_control) as FrameworkElement;
            }

            if (!String.IsNullOrEmpty(DisplayRootPath)) path_components.Add(DisplayRootPath);
            return String.Join("/", path_components.Reverse<string>());
        }

        public string SelectedPath
        {
            get { return selectedPath; }
            private set { selectedPath = value; NotifyPropertyChanged("SelectedPath"); }
        }
        string selectedPath;

        bool Dragging;
        Point DragStart;

        private void ElementHeader_StartDrag(object sender, MouseEventArgs e)
        {
            if (Dragging && (e.MouseDevice.GetPosition(sender as IInputElement) - DragStart).Length > 5)
            {
                var current_control = sender as FrameworkElement;
                do
                {
                    current_control = VisualTreeHelper.GetParent(current_control) as FrameworkElement;
                }
                while (current_control != null && !(current_control is TreeViewItem));
                var tree_item = current_control as TreeViewItem;

                var data = new DataObject("element", tree_item);

                DragDrop.DoDragDrop(tree_item, data, DragDropEffects.Link);

                Dragging = false;
            }
        }

        private void ElementHeader_Drop(object sender, DragEventArgs e)
        {
            var tree_item = e.Data.GetData("element") as TreeViewItem;
            if ((e.Source as FrameworkElement).DataContext == DmxTree.Items[0])
            {
                DisplayRootPath = MakeSelectionPath(tree_item);
                SetRoot(tree_item.DataContext as Datamodel.Element);
            }
        }

        private void ResetRoot_Click(object sender, MouseButtonEventArgs e)
        {
            DisplayRootPath = "";
            SetRoot(Datamodel.Root);
        }

        private void SetRoot(Datamodel.Element elem)
        {
            DmxTree.ItemContainerGenerator.StatusChanged += ExpandNode;
            DmxTree.ItemsSource = new Element[] { elem };
        }

        private void ElementHeader_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Dragging = false;
        }

        private void ElementHeader_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                DragStart = e.MouseDevice.GetPosition(sender as IInputElement);
                Dragging = true;
            }
            
        }
    }

    public class InspectPaneTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item == null) return null;

            var type = item.GetType();
            
            if (type == typeof(Datamodel.Element))
                return App.Current.Resources["ElementAttrs"] as DataTemplate;
            else
                return App.Current.Resources["ObjectList"] as DataTemplate;
        }
    }
}
