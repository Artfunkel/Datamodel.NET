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

        static DmxView()
        {
            DataContextProperty.OverrideMetadata(
                typeof(DmxView),
                new FrameworkPropertyMetadata(typeof(DmxView)));
        }

        private void DmxTree_Loaded(object sender, RoutedEventArgs e)
        {
            var tgv = ((Controls.TreeGridView)sender);

            var root_item = ((DmxPad.Controls.TreeGridViewItem)tgv.ItemContainerGenerator.ContainerFromIndex(0));
            if (root_item != null)
            {
                root_item.IsSelected = true;
                root_item.IsExpanded = true;
            }
        }

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
            ((ViewModel)DataContext).Path = MakeSelectionPath(e.OriginalSource as TreeViewItem);
        }

        private string MakeSelectionPath(TreeViewItem control)
        {
            List<string> path_components = new List<string>();
            object array_item = null;

            for (FrameworkElement current_control = control; !(current_control is TreeView); current_control = (FrameworkElement)VisualTreeHelper.GetParent(current_control))
            {
                if (!(current_control is TreeViewItem))
                    continue;

                var attr = current_control.DataContext as Datamodel.Attribute;
                var cattr = current_control.DataContext as ComparisonDatamodel.Attribute;

                if (attr == null && cattr == null)
                {
                    array_item = current_control.DataContext;
                    continue;
                }
                var name = attr != null ? attr.Name : cattr.Name;
                var array = (attr != null ? attr.Value : cattr.Value_Combined) as System.Collections.IEnumerable;

                if (array != null)
                {
                    if (array_item == null)
                    {
                        path_components.Add(name);
                        continue;
                    }

                    int i = 0;
                    foreach (var cur in array)
                    {
                        if (cur == array_item)
                        {
                            path_components.Add(name + String.Format("[{0}]", i));
                            array_item = null;
                        }
                        i++;
                    }
                    continue;
                }

                path_components.Add(name);
            }

            if (!String.IsNullOrEmpty(DisplayRootPath)) path_components.Add(DisplayRootPath);
            return "//" + String.Join("/", path_components.Reverse<string>());
        }

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
            SetRoot(((ViewModel)DataContext).Datamodel.Root);
        }

        private void SetRoot(Datamodel.Element elem)
        {
            DmxTree.ItemContainerGenerator.StatusChanged += ExpandNode;
            DmxTree.ItemsSource = new Element[] { elem };

            PathBox.Text = "//"; // TODO: element ID for non-root elements
            PathBox_SourceUpdated(PathBox, null);
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

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var attr = ((Button)sender).DataContext as Datamodel.Attribute;
            if (attr != null)
                attr.Owner.Remove(attr);
        }

        private void ChooseElement_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var attr = (Datamodel.Attribute)DmxTree.SelectedItem;

            var select = new Controls.SelectElement();
            select.Owner = App.Current.MainWindow;
            select.SelectedElement = (Element)attr.Value;
            select.DataContext = DataContext;

            if (select.ShowDialog() == true)
            {
                attr.Value = select.SelectedElement;
                e.Handled = true;
            }
        }

        private void GUIDCopy_Click(object sender, RoutedEventArgs e)
        {
            var elem = ((Element)((Hyperlink)sender).CommandParameter);
            System.Windows.Clipboard.SetText(elem.ID.ToString());
            e.Handled = true;
        }

        private void PathBox_SourceUpdated(object sender, DataTransferEventArgs e)
        {
            var path = ((TextBox)sender).Text;
            // TODO: select item specified by the path Converters.DatamodelPath.Convert

            if (e != null) e.Handled = true;
        }

        private void PathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                ((TextBox)sender).GetBindingExpression(TextBox.TextProperty).UpdateSource();
                e.Handled = true;
            }
        }

        bool ShowChangesOnly = false;

        private void ShowChangesOnly_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ShowChangesOnly = (bool)e.Parameter;
            DmxTree_Comparison.Items.Refresh();
        }

        private bool ShowChangesOnly_Filter(object obj)
        {
            if (!ShowChangesOnly) return true;

            var attr = obj as ComparisonDatamodel.Attribute;
            if (attr != null)
                return attr.State != ComparisonDatamodel.ComparisonState.Unchanged;

            var elem = obj as ComparisonDatamodel.Element;
            if (elem != null)
                return elem.State != ComparisonDatamodel.ComparisonState.Unchanged;

            throw new InvalidOperationException();
        }

        private void ComparisonViewItem_Loaded(object sender, RoutedEventArgs e)
        {
            ((Controls.TreeGridViewItem)sender).Items.Filter = ShowChangesOnly_Filter;
        }

    }

    public class InspectPaneTemplateSelector : DataTemplateSelector
    {
        static DataTemplate ObjectList = (DataTemplate)App.Current.Resources["Attr_List"];

        static DataTemplate GenericSingle = (DataTemplate)App.Current.Resources["Attr_Generic"];
        static DataTemplate ElementSingle = (DataTemplate)App.Current.Resources["Attr_Element"];
        static DataTemplate BoolSingle = (DataTemplate)App.Current.Resources["Attr_Bool"];

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item == null) return null;

            var attr = item as Datamodel.Attribute;
            if (attr != null)
            {
                var inner_type = attr.Value == null ? typeof(Element) : attr.Value.GetType();

                if (Datamodel.Datamodel.IsDatamodelArrayType(inner_type))
                    return ObjectList;
                if (inner_type == typeof(Element))
                    return ElementSingle;
                if (inner_type == typeof(bool))
                    return BoolSingle;
            }
            return GenericSingle;
        }
    }
}
