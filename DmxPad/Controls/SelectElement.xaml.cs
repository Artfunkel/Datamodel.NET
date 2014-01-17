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
using System.Windows.Shapes;

using Datamodel;

namespace DmxPad.Controls
{
    /// <summary>
    /// Interaction logic for SelectElement.xaml
    /// </summary>
    public partial class SelectElement : Window
    {
        public SelectElement()
        {
            InitializeComponent();
            ElementList.Focus();

            Search.FilterPredicate = item => ((Element)item).Name.ContainsCI(Search.Terms);
        }

        public Element SelectedElement
        {
            get { return (Element)ElementList.SelectedItem; }
            set { ElementList.SelectedItem = value; }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
