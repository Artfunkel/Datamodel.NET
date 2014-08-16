using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using Datamodel;
using System.IO;
using DmAttr = System.Collections.Generic.KeyValuePair<string, object>;

namespace DmxPad
{

    public class ViewModel : INotifyPropertyChanged, IDisposable
    {
        public ViewModel()
        {

        }
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
            get { return _DisplayRoot ?? (Datamodel != null ? Datamodel.Root : null); }
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

    public class AttributeView : INotifyPropertyChanged
    {
        public AttributeView(Element elem, string key, int index = -1)
        {
            _Owner = elem;
            _Key = key;
            Index = index;
        }

        public string Key
        {
            get { return _Key; }
            set
            {
                _Key = value;
                OnPropertyChanged("Key");
                throw new NotImplementedException("Cannot rename attributes yet."); // TODO
            }
        }
        string _Key;

        public int Index { get; protected set; }

        public object Value
        {
            get
            {
                var value = Owner[Key];

                if (Index < 0) return value;

                var array = (System.Collections.IList)value;
                
                if (Index >= array.Count)
                    return null; // TODO: account for comparison array changes
                
                return array[Index];
            }
            set
            {
                var value_type = value == null ? typeof(Element) : value.GetType();

                if (value_type != ValueType)
                {
                    var converter = TypeDescriptor.GetConverter(ValueType);
                    if (converter != null)
                    {
                        value = converter.ConvertFrom(null, System.Globalization.CultureInfo.CurrentUICulture, value);
                    }
                }

                Owner[Key] = value;
                OnPropertyChanged("Value");
            }
        }

        public virtual Type ValueType
        {
            get { return Value == null ? typeof(Element) : Value.GetType(); }
            set
            {
                if (!Datamodel.Datamodel.AttributeTypes.Contains(value)) throw new Datamodel.AttributeTypeException("Unsupported type");
                if (!Value.GetType().IsAssignableFrom(value))
                {
                    throw new NotImplementedException("Cannot change attribute types yet."); // TODO
                }
            }
        }

        public Element Owner
        {
            get { return _Owner; }
            set { _Owner = value; OnPropertyChanged("Owner"); }
        }
        Element _Owner;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string property)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(property));
        }
    }

    namespace Converters
    {
        public class ElementAttributesWrapper : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var elem = value as Element;
                if (elem != null)
                    return new ObservableCollection<AttributeView>(elem.Select(a => new AttributeView(elem, a.Key)));

                var elem_array = value as IEnumerable<Element>;
                if (elem_array != null)
                    return new ObservableCollection<Element>(elem_array);

                return null;
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var attrs = value as IEnumerable<AttributeView>;
                if (attrs != null)
                    return attrs.Select(a => new DmAttr(a.Key, a.Value));

                var elem_array = value as IEnumerable<Element>;
                if (elem_array != null)
                    return elem_array;

                return null;
            }
        }

    }
}
