using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;

using Datamodel;
using DmEncoding = System.Tuple<string, int>;
using ComparisonState = DmxPad.ComparisonDatamodel.ComparisonState;

namespace DmxPad.Converters
{
    public class Debug : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            System.Diagnostics.Debugger.Break();
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            System.Diagnostics.Debugger.Break();
            return value;
        }
    }

    public class ChildPath : IValueConverter
    {
        static object[] Empty = { };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var elem = value as Element;
            if (elem != null) return elem;

            var attr = value as AttributeView;
            if (attr != null)
            {
                var t = attr.ValueType;
                if (Datamodel.Datamodel.IsDatamodelArrayType(t))
                    t = Datamodel.Datamodel.GetArrayInnerType(t);
                if (t == typeof(Datamodel.Element))
                    return attr.Value;
            }

            var celem = value as ComparisonDatamodel.Element;
            if (celem != null) return celem.State != ComparisonState.Removed ? celem : null;

            var cattr = value as ComparisonDatamodel.Attribute;
            if (cattr != null && cattr.Value_Combined != null)
            {
                var t = cattr.Value_Combined.GetType();
                var inner_t = Datamodel.Datamodel.GetArrayInnerType(t);

                if (t == typeof(ComparisonDatamodel.Element) || inner_t == typeof(ComparisonDatamodel.Element))
                    return cattr.Value_Combined;
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ValueColumnTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var attr = item as AttributeView;
            if (attr == null || attr.Value == null || attr.Value is Element || Datamodel.Datamodel.IsDatamodelArrayType(attr.Value.GetType()))
                return null;

            return App.Current.Resources["AttrValueTemplate"] as DataTemplate;
        }
    }

    public class ValueColumnWidth : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var attr = value as AttributeView;
            var elem = value as Datamodel.Element;
            Type type;

            if (attr != null)
                type = attr.ValueType;
            else if (elem != null)
                type = typeof(Element);
            else return 0;

            if (type == typeof(Element) || (Datamodel.Datamodel.IsDatamodelArrayType(type) && Datamodel.Datamodel.GetArrayInnerType(type) != typeof(Element)))
                return new GridLength(1, GridUnitType.Star);
            return 0;

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ValuePanelVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var width = (GridLength)value;
            if (width.Value == 0)
                return Visibility.Hidden;
            else return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AttributeIcon : IValueConverter
    {
        static Dictionary<string, ImageSource> IconSources = new Dictionary<string, ImageSource>()
        {
            { "element", new BitmapImage(new Uri("/DmxPad;component/Resources/Element.png", UriKind.Relative))},
            { "attribute", new BitmapImage(new Uri("/DmxPad;component/Resources/Attribute.png", UriKind.Relative))},
            { "array", new BitmapImage(new Uri("/DmxPad;component/Resources/Array.png", UriKind.Relative))},

        };
        static Image GetIcon(string name)
        {
            return new Image()
            {
                Source = IconSources[name],
                Stretch = Stretch.None
            };
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AttributeView)
            {
                var attr = (AttributeView)value;
                if (attr.Key != null) value = attr.Value;
            }
            else
            {
                var cattr = value as ComparisonDatamodel.Attribute;
                if (cattr != null)
                    value = cattr.Value_Combined;
            }

            Image base_icon;
            if (value == null || value is Element || value is IList<Element> ||
                value is ComparisonDatamodel.Element || value is IList<ComparisonDatamodel.Element>)
            {
                base_icon = GetIcon("element");
                if (value == null)
                    base_icon.Opacity = 0.5;
            }
            else
                base_icon = GetIcon("attribute");

            if (value != null && Datamodel.Datamodel.IsDatamodelArrayType(value.GetType()))
            {
                var canvas = new Grid();
                canvas.Children.Add(base_icon);
                canvas.Children.Add(GetIcon("array"));
                return canvas;
            }
            else
                return base_icon;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class VisibleIfNotNull : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null || System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject()) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class GetFriendlyTypeName : IValueConverter
    {
        static readonly Dictionary<Type, string> TypeNames = new Dictionary<Type, string>();

        static GetFriendlyTypeName()
        {
            TypeNames[typeof(Element)] = "Element";
            TypeNames[typeof(int)] = "int";
            TypeNames[typeof(float)] = "float";
            TypeNames[typeof(bool)] = "bool";
            TypeNames[typeof(string)] = "string";
            TypeNames[typeof(byte[])] = "binary";
            TypeNames[typeof(TimeSpan)] = "time";
            TypeNames[typeof(System.Drawing.Color)] = "color";
            TypeNames[typeof(Vector2)] = "vector2";
            TypeNames[typeof(Vector3)] = "vector3";
            TypeNames[typeof(Vector4)] = "vector4";
            TypeNames[typeof(Angle)] = "angle";
            TypeNames[typeof(Quaternion)] = "quaternion";
            TypeNames[typeof(Datamodel.Matrix)] = "matrix";
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Type)
                return TypeNames[(Type)value];

            var attr = value as AttributeView;
            if (attr != null)
            {
                value = attr.Value;
            }
            else
            {
                var cattr = value as ComparisonDatamodel.Attribute;
                if (cattr != null)
                    value = cattr.Value_Combined;
            }

            if (value == null) return "Element (unset)";

            var type = value.GetType();
            bool array = false;
            if (type.IsGenericType)
            {
                array = true;
                type = type.GetGenericArguments()[0];
            }

            if ((type == typeof(Element) || type == typeof(ComparisonDatamodel.Element)) && !array)
            {
                var tb = new TextBlock();
                tb.SetBinding(TextBlock.TextProperty, new Binding()
                    {
                        Source = value,
                        Path = new PropertyPath("ClassName"),
                    });
                return tb;
            }

            string type_name;

            if (type == typeof(byte[]))
                type_name = "Binary";
            else if (type == typeof(float))
                type_name = "Float";
            else
                type_name = type.Name;

            return array ? type_name + " array" : type_name;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class VectorToString : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return String.Join(" ", values);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            try
            {
                var floats = (value as string).Split(' ').Take(targetTypes.Length).Select(s => float.Parse(s));
                return floats.Concat(Enumerable.Repeat(0f, targetTypes.Length - floats.Count())).Cast<object>().ToArray();
            }
            catch
            {
                return Enumerable.Repeat(DependencyProperty.UnsetValue, targetTypes.Length).ToArray();
            }
        }
    }

    public class GetAttributeValue : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var attr = value as AttributeView;
            if (attr != null && attr.Value != null)
            {
                var inner_type = Datamodel.Datamodel.GetArrayInnerType(attr.Value.GetType());
                if (inner_type != null && inner_type.IsValueType)
                {
                    var len = (attr.Value as System.Collections.IList).Count;
                    var nullable_type = typeof(Nullable<>).MakeGenericType(inner_type);
                    var nullable_constructor = nullable_type.GetConstructor(new Type[] { inner_type });
                    var nullable_array = nullable_type.MakeArrayType().GetConstructor(new Type[] { typeof(int) }).Invoke(new Object[] { len }) as System.Collections.IList;
                    int i = 0;
                    foreach (var item in attr.Value as System.Collections.IEnumerable)
                    {
                        nullable_array[i] = nullable_constructor.Invoke(new object[] { item });
                        i++;
                    }
                    return nullable_array;
                }
                return attr.Value;
            }
            else return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EnsureElement : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Element)
                return value;

            var dm = value as Datamodel.Datamodel;
            if (dm != null)
                return new object[] { dm.Root };

            var attr = value as AttributeView;
            if (attr != null)
                return attr.Owner;

            var cdm = value as ComparisonDatamodel;
            if (cdm != null)
                return new object[] { cdm.Root };

            var cattr = value as ComparisonDatamodel.Attribute;
            if (cattr != null)
                return cattr.Owner;

            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EnsureAttributeView : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is AttributeView ? value : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }


    public class Encoding : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return new DmEncoding(values[0] as string, values[1] == DependencyProperty.UnsetValue ? 0 : (int)values[1]);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            var t = (DmEncoding)value;
            return new object[] { t.Item1, t.Item2 };
        }
    }

    public class EncodingDisplay : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            var t = (DmEncoding)value;
            return String.Join(" ", t.Item1, t.Item2);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    class AttributeGroupDisplay : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is AttributeView ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DatamodelPath : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var dm = values[0] as Datamodel.Datamodel;
            if (dm == null) return null;

            var path = (string)values[1];
            if (path == String.Empty) return null;

            object current;
            if (path.StartsWith("//"))
            {
                current = dm.Root;
                path = path.Remove(0, 2);
            }
            else
                throw new NotImplementedException();

            foreach (var part in path.Split('/').Where(p => p.Length > 0))
            {
                var attr = current as AttributeView;
                if (attr != null && attr.Value is Element)
                {
                    current = attr.Value;
                    attr = null;
                }

                var elem = current as Element;
                if (elem == null)
                    break;

                var name = part;
                var index = -1;

                var indexer_pos = part.LastIndexOf('[');
                if (indexer_pos != -1)
                {
                    name = part.Substring(0, indexer_pos);
                    index = Int32.Parse(part.Substring(indexer_pos + 1, part.Length - indexer_pos - 2));
                }

                if (!elem.ContainsKey(name)) return null;

                current = attr = new AttributeView(elem, name);
                if (index != -1)
                    current = ((System.Collections.IList)attr.Value)[index];
            }

            return current;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ComparisonTreeVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ComparisonIcon : IValueConverter
    {
        struct ComparisonStateData
        {
            public ComparisonStateData(string image, string tooltip)
            {
                ImageSource = new BitmapImage(new Uri(String.Format("/DmxPad;component/Resources/{0}.png", image), UriKind.Relative));
                ToolTip = tooltip;
            }

            public readonly ImageSource ImageSource;
            public readonly string ToolTip;
        }
        static Dictionary<ComparisonState, ComparisonStateData> StateData = new Dictionary<ComparisonState, ComparisonStateData>()
        {
            { ComparisonState.Changed, new ComparisonStateData("Changed", "This item is different") },
            { ComparisonState.ChildChanged, new ComparisonStateData("ChildChanged", "One or more children of this item are different") },
            { ComparisonState.Added, new ComparisonStateData("Added", "This item was added") },
            { ComparisonState.Removed, new ComparisonStateData("Removed", "This item was removed") },
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var state = (ComparisonState)value;

            if (state == ComparisonState.Unchanged)
                return null;

            var data = StateData[state];
            return new Image()
            {
                Source = data.ImageSource,
                ToolTip = data.ToolTip,
                Width = 16,
                Height = 16,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AttributeListView : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null && Datamodel.Datamodel.IsDatamodelArrayType(value.GetType()) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DmxTreeItemSource : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var dm = value as Datamodel.Datamodel;

            if (dm != null)
                return new object[] { dm.Root };

            var cdm = value as ComparisonDatamodel;
            if (cdm != null)
                return new object[] { cdm.Root };

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NotNull : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseBoolean : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(bool)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(bool)value;
        }
    }

    public class CollectionTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var attr = item as AttributeView;
            if (attr == null) return null;

            if (!Datamodel.Datamodel.IsDatamodelArrayType(attr.ValueType)) return null;

            var host = (FrameworkElement)container;

            var inner = Datamodel.Datamodel.GetArrayInnerType(attr.ValueType);
            string resource_name;

            if (inner == typeof(Vector2))
                resource_name = "Vector2";
            else if (inner == typeof(Vector3) || inner == typeof(Angle))
                resource_name = "Vector3";
            else if (inner == typeof(Vector4) || inner == typeof(Quaternion))
                resource_name = "Vector4";
            else
                resource_name = "Solo";

            return (DataTemplate)host.Resources[resource_name];
        }
    }
}