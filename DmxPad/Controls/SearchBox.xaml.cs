using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.ComponentModel;

namespace DmxPad.Controls
{
    public partial class SearchBox : UserControl
    {
        public SearchBox()
        {
            InitializeComponent();
        }

        public new bool Focus()
        {
            return Input.Focus();
        }

        /// <summary>
        /// Gets a cleaned version of the current search term(s).
        /// </summary>
        public string Terms { get { return Input.Text.ToLower(); } }

        /// <summary>
        /// Gets or sets the element which this SearchBox is filtering.
        /// </summary>
        [Description("Gets or sets the ItemsControl element which this SearchBox should target."), Category("Common Properties")] 
        public ItemsControl SearchTarget
        {
            get { return (ItemsControl)GetValue(SearchTargetProperty); }
            set { SetValue(SearchTargetProperty, value); }
        }
        public static readonly DependencyProperty SearchTargetProperty =
            DependencyProperty.Register("SearchTarget", typeof(ItemsControl), typeof(SearchBox));

        /// <summary>
        /// Gets or sets the logic by which <see cref="SearchTarget"/> is filtered.
        /// </summary>
        [Description("Gets or sets the method which is used to filter the SearchTarget."), Category("Common Properties")] 
        public Predicate<object> FilterPredicate
        {
            get { return (Predicate<object>)GetValue(SearchPredicateProperty); }
            set { SetValue(SearchPredicateProperty, value); }
        }
        public static readonly DependencyProperty SearchPredicateProperty =
            DependencyProperty.Register("SearchPredicate", typeof(Predicate<object>), typeof(SearchBox), new PropertyMetadata(null));

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchTarget != null)
                SearchTarget.Items.Filter = Input.Text.Any() ? FilterPredicate : null;
            if (TextChanged != null)
                TextChanged(this, e);
        }

        public event TextChangedEventHandler TextChanged;
    }

    public class SearchHintVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null || ((string)value).Length == 0 ? Visibility.Visible : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public static class SearchBoxExtensions
    {
        /// <summary>
        /// Case-insensitive string comparison
        /// </summary>
        public static bool ContainsCI(this string source, string toCheck)
        {
            return source.IndexOf(toCheck, StringComparison.InvariantCultureIgnoreCase) >= 0;
        }
    }
}
