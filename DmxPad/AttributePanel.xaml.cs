using System;
using System.Collections.Generic;
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
    public partial class AttributePanel : UserControl
    {
        public AttributePanel()
        {
            InitializeComponent();
        }

        public System.IO.FileInfo File
        {
            get { return (System.IO.FileInfo)GetValue(FileProperty); }
            set { SetValue(FileProperty, value); }
        }
        public static readonly DependencyProperty FileProperty =
            DependencyProperty.Register("File", typeof(System.IO.FileInfo), typeof(AttributePanel), new PropertyMetadata(null));
        
        public object Selected
        {
            get { return (object)GetValue(SelectedProperty); }
            set { SetValue(SelectedProperty, value); }
        }
        public static readonly DependencyProperty SelectedProperty =
            DependencyProperty.Register("Selected", typeof(object), typeof(AttributePanel), new PropertyMetadata(null));


        private void GUIDCopy_Click(object sender, RoutedEventArgs e)
        {
            var elem = ((Element)((Hyperlink)sender).CommandParameter);
            System.Windows.Clipboard.SetText(elem.ID.ToString());
            e.Handled = true;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var attr = ((Button)sender).DataContext as AttributeView;
            if (attr != null)
                attr.Owner.Remove(attr.Key);
        }
    }
}
