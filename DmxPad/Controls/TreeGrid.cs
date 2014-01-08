using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;

namespace DmxPad.Controls
{
    /// <summary>
    /// Represents a control that displays hierarchical data in a grid structure that has items that can expand and collapse.
    /// </summary>
    [StyleTypedProperty(Property = "ItemContainerStyle", StyleTargetType = typeof(TreeGridViewItem))]
    [TemplatePart(Name = "PART_Grid", Type = typeof(Grid))]
    public class TreeGridView : TreeView
    {
        public TreeGridView()
        {
            ColumnDefinitions = new List<TreeGridColumnDefinition>();
        }
        static TreeGridView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TreeGridView), new FrameworkPropertyMetadata(typeof(TreeGridView)));
        }

        #region Methods
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (Template == null) return;

            var grid = GetTemplateChild("PART_Grid") as Grid;
            if (grid == null)
                System.Diagnostics.Trace.TraceError("The template of TreeGridView {0} does not contain a Grid element with name \"PART_Grid\".", this.GetHashCode());
            else
            {
                foreach (var def in ColumnDefinitions)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition() { SharedSizeGroup = "col_" + def.GetHashCode().ToString() });
                    var header = new TreeGridViewCell() { Content = def.Header };
                    grid.Children.Add(header);
                    Grid.SetColumn(header, grid.ColumnDefinitions.Count - 1);
                }
            }
        }

        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is TreeGridViewItem;
        }
        protected override DependencyObject GetContainerForItemOverride()
        {
            return new TreeGridViewItem();
        }

        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            if (TitleTemplate != null)
                ((TreeGridViewItem)element).Title = TitleTemplate.LoadContent();
        }
        #endregion

        #region Properties

        [Description("Sets whether the header row is displayed."), Category("Common Properties")]
        public bool ShowHeaders
        {
            get { return (bool)GetValue(ShowHeadersProperty); }
            set { SetValue(ShowHeadersProperty, value); }
        }
        public static readonly DependencyProperty ShowHeadersProperty = DependencyProperty.Register("ShowHeaders", typeof(bool), typeof(TreeGridView), new PropertyMetadata(true));

        [Description("Sets the Binding path used to locate the children of each bound item."), Category("Common Properties")]
        public string ChildItemsPath
        {
            get { return (string)GetValue(ChildItemsPathProperty); }
            set { SetValue(ChildItemsPathProperty, value); }
        }
        public static readonly DependencyProperty ChildItemsPathProperty = DependencyProperty.Register("ChildItemsPath", typeof(string), typeof(TreeGridView), new PropertyMetadata(null));

        [Description("A deferred binding which locates the children of each bound item."), Category("Common Properties")]
        public Binding ChildItemsSelector
        {
            get { return ChildItemsSelectorProperty; }
            set { ChildItemsSelectorProperty = value; }
        }
        Binding ChildItemsSelectorProperty;

        [Description("Sets the template used to generate the Title cell of each item."), Category("Common Properties")]
        public DataTemplate TitleTemplate
        {
            get { return (DataTemplate)GetValue(TitleTemplateProperty); }
            set { SetValue(TitleTemplateProperty, value); }
        }
        public static readonly DependencyProperty TitleTemplateProperty =
            DependencyProperty.Register("TitleTemplate", typeof(DataTemplate), typeof(TreeGridView), new PropertyMetadata(null));

        [Description("Sets the content that appears as the header of the Title column."), Category("Common Properties")]
        public object TitleColumnHeader
        {
            get { return (object)GetValue(TitleColumnHeaderProperty); }
            set { SetValue(TitleColumnHeaderProperty, value); }
        }
        public static readonly DependencyProperty TitleColumnHeaderProperty =
            DependencyProperty.Register("TitleColumnHeader", typeof(object), typeof(TreeGridView), new PropertyMetadata(null));

        [Description("Defines the columns that appear in the TreeGridView after the Title column."), Category("Common Properties")]
        public List<TreeGridColumnDefinition> ColumnDefinitions
        {
            get { return (List<TreeGridColumnDefinition>)GetValue(ColumnDefinitionsProperty); }
            set { SetValue(ColumnDefinitionsProperty, value); }
        }
        public static readonly DependencyProperty ColumnDefinitionsProperty =
            DependencyProperty.Register("ColumnDefinitions", typeof(List<TreeGridColumnDefinition>), typeof(TreeGridView), new PropertyMetadata(null));

        [Description("Sets the gap between each column."), Category("Common Properties")]
        public double ColumnSpacing
        {
            get { return (double)GetValue(ColumnSpacingProperty); }
            set { SetValue(ColumnSpacingProperty, value); }
        }
        public static readonly DependencyProperty ColumnSpacingProperty =
            DependencyProperty.Register("ColumnSpacing", typeof(double), typeof(TreeGridView), new PropertyMetadata(10.0));
        #endregion
    }

    internal class CellMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return default(Thickness);

            double horizontal = ((double)value) / 2;
            double vertical = parameter != null ? double.Parse((string)parameter) / 2 : 0;
            
            return new Thickness(horizontal, vertical, horizontal, vertical);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var thickness = (Thickness)value;
            return (thickness.Top + thickness.Bottom) / 2;
        }
    }

    public class TreeGridColumnDefinition : DependencyObject
    {
        [Description("The DataTemplate used to to create grid cells for this column definition."), Category("Common Properties")]
        public DataTemplate ItemTemplate
        {
            get { return (DataTemplate)GetValue(ItemTemplateProperty); }
            set { SetValue(ItemTemplateProperty, value); }
        }
        public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register("ItemTemplate", typeof(DataTemplate), typeof(TreeGridColumnDefinition), new PropertyMetadata(null));

        /// <summary>
        /// Gets or sets the custom logic for choosing a template used to display each item.
        /// </summary>
        public DataTemplateSelector ItemTemplateSelector
        {
            get { return (DataTemplateSelector)GetValue(ItemTemplateSelectorProperty); }
            set { SetValue(ItemTemplateSelectorProperty, value); }
        }
        public static readonly DependencyProperty ItemTemplateSelectorProperty =
            DependencyProperty.Register("ItemTemplateSelector", typeof(DataTemplateSelector), typeof(TreeGridColumnDefinition), new PropertyMetadata(null));

        [Description("Sets the content that appears as the header of the column created for this column definiton."), Category("Common Properties")]
        public object Header
        {
            get { return (object)GetValue(HeaderProperty); }
            set { SetValue(HeaderProperty, value); }
        }
        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register("Header", typeof(object), typeof(TreeGridBoundColumnDefinition), new PropertyMetadata(null));

        /// <summary>
        /// Gets or sets the style that is used to render cells in the column.
        /// </summary>
        [Description("Gets or sets the style that is used to render cells in the column."), Category("Common Properties")]
        public Style CellStyle
        {
            get { return (Style)GetValue(CellStyleProperty); }
            set { SetValue(CellStyleProperty, value); }
        }
        public static readonly DependencyProperty CellStyleProperty = DependencyProperty.Register("CellStyle", typeof(Style), typeof(TreeGridColumnDefinition), new PropertyMetadata(null));
    }

    public abstract class TreeGridBoundColumnDefinition : TreeGridColumnDefinition
    {
        [Description("A deferred Binding used as the DataContext of grid cells created for this column definition."), Category("Common Properties")]
        public Binding Binding
        {
            get { return _Binding; }
            set { _Binding = value; }
        }
        Binding _Binding;
    }

    public class TreeGridTextColumnDefinition : TreeGridBoundColumnDefinition
    {
        static TreeGridTextColumnDefinition()
        {
            var template = new DataTemplate();
            template.VisualTree = new FrameworkElementFactory(typeof(TextBlock));
            template.VisualTree.SetBinding(TextBlock.TextProperty, new Binding());
            template.Seal();

            ItemTemplateProperty.OverrideMetadata(typeof(TreeGridTextColumnDefinition),
                new PropertyMetadata(template));
        }
    }

    public class TreeGridContentColumnDefinition : TreeGridBoundColumnDefinition
    {
        static TreeGridContentColumnDefinition()
        {
            var template = new DataTemplate();
            template.VisualTree = new FrameworkElementFactory(typeof(ContentControl));
            template.VisualTree.SetBinding(ContentControl.ContentProperty, new Binding());
            template.Seal();

            ItemTemplateProperty.OverrideMetadata(typeof(TreeGridContentColumnDefinition),
                new PropertyMetadata(template));
        }
    }

    /// <summary>
    /// Implements a selectable item in a <see cref="TreeGridView"/> control.
    /// </summary>
    [StyleTypedProperty(Property = "ItemContainerStyle", StyleTargetType = typeof(TreeViewItem))]
    [TemplatePart(Name = "PART_Grid", Type = typeof(Grid))]
    public class TreeGridViewItem : TreeViewItem
    {
        static TreeGridViewItem()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TreeGridViewItem), new FrameworkPropertyMetadata(typeof(TreeGridViewItem)));
        }

        #region Methods
        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is TreeGridViewItem;
        }
        protected override DependencyObject GetContainerForItemOverride()
        {
            return new TreeGridViewItem();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var grid = GetTemplateChild("PART_Grid") as Grid;
            if (grid == null)
                System.Diagnostics.Trace.TraceError("The template of TreeGridViewItem {0} does not contain a Grid element with name \"PART_Grid\".", this.GetHashCode());
            else
            {
                foreach (var def in Owner.ColumnDefinitions)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition() { SharedSizeGroup = "col_" + def.GetHashCode().ToString() });
                    var template = def.ItemTemplateSelector == null ? def.ItemTemplate : def.ItemTemplateSelector.SelectTemplate(DataContext, this);
                    if (template != null)
                    {
                        var cell = new TreeGridViewCell();
                        cell.Content = template.LoadContent() as FrameworkElement;
                        cell.Style = def.CellStyle;
                        if (def is TreeGridBoundColumnDefinition)
                            BindingOperations.SetBinding(cell, FrameworkElement.DataContextProperty, ((TreeGridBoundColumnDefinition)def).Binding);
                        grid.Children.Add(cell);
                        Grid.SetColumn(cell, grid.ColumnDefinitions.Count - 1);
                    }
                }
            }
        }

        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            var template = TitleTemplate;
            if (TitleTemplate == null)
            {
                DependencyObject cur_container = this;
                while (true)
                {
                    cur_container = ItemsControlFromItemContainer(cur_container);
                    if (cur_container == null)
                        break;
                    if (cur_container is TreeGridView)
                    {
                        template = ((TreeGridView)cur_container).TitleTemplate;
                        break;
                    }
                    if (cur_container is TreeGridViewItem)
                    {
                        template = ((TreeGridViewItem)cur_container).TitleTemplate;
                        if (template != null) break;
                    }
                }
            }
            if (template != null)
                ((TreeGridViewItem)element).Title = template.LoadContent();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            DependencyObject cur_container = this;
            while (true)
            {
                cur_container = ItemsControlFromItemContainer(cur_container);
                if (cur_container == null || cur_container is TreeGridView)
                    break;
                else if (cur_container is TreeGridViewItem)
                    Indent++;
            }
            if (Owner.ChildItemsSelector != null)
                BindingOperations.SetBinding(this, ItemsSourceProperty, Owner.ChildItemsSelector ?? new Binding(Owner.ChildItemsPath));
        }
        #endregion

        #region Properties
        /// <summary>
        /// Gets the <see cref="TreeGridView"/> that contains this object.
        /// </summary>
        public TreeGridView Owner
        {
            get
            {
                DependencyObject cur_container = this;
                while (cur_container != null)
                {
                    cur_container = ItemsControlFromItemContainer(cur_container);
                    if (cur_container is TreeGridView)
                        return (TreeGridView)cur_container;
                }
                return null;
            }
        }

        /// <summary>
        /// Gets the indentation level of this TreeGridViewItem.
        /// </summary>
        public int Indent { get { return _Indent; } protected set { _Indent = value; } }
        int _Indent = new int();

        /// <summary>
        /// Gets or sets the Title content of this TreeGridViewItem.
        /// </summary>
        public object Title
        {
            get { return (object)GetValue(TreeViewHeaderProperty); }
            set { SetValue(TreeViewHeaderProperty, value); }
        }
        public static readonly DependencyProperty TreeViewHeaderProperty = DependencyProperty.Register("Title", typeof(object), typeof(TreeGridViewItem), new PropertyMetadata(null));

        /// <summary>
        /// Gets or sets the <see cref="DataTemplate"/> used to generate the Title cell for this TreeGridViewItem.
        /// </summary>
        public DataTemplate TitleTemplate
        {
            get { return (DataTemplate)GetValue(TreeGridViewHeaderTemplateProperty); }
            set { SetValue(TreeGridViewHeaderTemplateProperty, value); }
        }
        public static readonly DependencyProperty TreeGridViewHeaderTemplateProperty = DependencyProperty.Register("TitleTemplate", typeof(DataTemplate), typeof(TreeGridViewItem), new PropertyMetadata(null));
        #endregion
    }

    public class TreeGridViewCell : ContentControl
    { }

    internal class ExpanderVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return ((bool)value) ? Visibility.Hidden : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    internal class ExpanderMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return new Thickness((int)value * 16, 0, 5, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
