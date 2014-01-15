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

namespace DmxPad.Controls
{
    /// <summary>
    /// Interaction logic for LabelledControl.xaml
    /// </summary>
    public partial class LabelledControl : UserControl
    {
        public LabelledControl()
        {
            InitializeComponent();
        }

        static LabelledControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(LabelledControl),
                new FrameworkPropertyMetadata(typeof(LabelledControl)));
        }

        public string LabelText
        {
            get { return (string)GetValue(LabelTextProperty); }
            set { SetValue(LabelTextProperty, value); }
        }
        public static readonly DependencyProperty LabelTextProperty =
            DependencyProperty.Register("LabelText", typeof(string), typeof(LabelledControl), new PropertyMetadata("FOO"));

        public GridLength LabelWidth
        {
            get { return (GridLength)GetValue(LabelWidthProperty); }
            set { SetValue(LabelWidthProperty, value); }
        }

        // Using a DependencyProperty as the backing store for LabelWidth.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty LabelWidthProperty =
            DependencyProperty.Register("LabelWidth", typeof(GridLength), typeof(LabelledControl), new PropertyMetadata(GridLength.Auto));

        
    }
}
