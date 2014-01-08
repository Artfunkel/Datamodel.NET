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

namespace DmxPad
{
    public class DebugConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    public class ChildPathConverter : IValueConverter
    {
        static object[] Empty = { };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var elem = value as Element;
            if (elem != null) return elem;

            var attr = value as Datamodel.Attribute;
            if (attr != null && attr.Value != null)
            {
                var t = attr.Value.GetType();
                if (Datamodel.Datamodel.IsDatamodelArrayType(t))
                    t = Datamodel.Datamodel.GetArrayInnerType(t);
                if (t == typeof(Datamodel.Element))
                    return attr.Value;
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
            var attr = item as Datamodel.Attribute;
            if (attr == null || attr.Value == null || attr.Value is Element || Datamodel.Datamodel.IsDatamodelArrayType(attr.Value.GetType()))
                return null;

            return App.Current.Resources["AttrValueTemplate"] as DataTemplate;
        }
    }

    public class ValueColumnWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var attr = value as Datamodel.Attribute;
            var elem = value as Datamodel.Element;
            Type type;

            if (attr != null && attr.Value != null)
                type = attr.Value.GetType();
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

    public class ValuePanelVisibilityConverter : IValueConverter
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

    public class AttributeIconConverter : IValueConverter
    {
        static Image GetIcon(string name)
        {
            return new Image()
            {
                Source = new BitmapImage(new Uri(String.Format("/DmxPad;component/Resources/{0}.png", name), UriKind.Relative)),
                SnapsToDevicePixels = true
            };
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Datamodel.Attribute)
                value = (value as Datamodel.Attribute).Value;

            Image base_icon;
            if (value == null || value is Element || value is ICollection<Element>)
            {
                base_icon = GetIcon("element");
                if (value == null)
                    base_icon.Opacity = 0.5;
            }
            else
                base_icon = GetIcon("attribute");

            if (value != null && value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition().GetInterface("ICollection`1") != null)
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
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ExtractFileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (String.IsNullOrEmpty(value as string))
                return "Unsaved";
            return System.IO.Path.GetFileName(value as string);
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
            if (value is Datamodel.Attribute) value = (value as Datamodel.Attribute).Value;
            if (value == null) return "Element (unset)";

            var type = value.GetType();
            bool array = false;
            if (type.IsGenericType)
            {
                array = true;
                type = type.GetGenericArguments()[0];
            }

            if (type == typeof(Element) && !array)
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
            var attr = value as Datamodel.Attribute;
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
}