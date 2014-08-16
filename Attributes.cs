using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Globalization;

using AttrKVP = System.Collections.Generic.KeyValuePair<string, object>;

namespace Datamodel
{
    /// <summary>
    /// A name/value pair associated with an <see cref="Element"/>.
    /// </summary>
    struct Attribute
    {
        /// <summary>
        /// Creates a new Attribute with a specified name and value.
        /// </summary>
        /// <param name="name">The name of the Attribute, which must be unique to its owner.</param>
        /// <param name="value">The value of the Attribute, which must be of a supported Datamodel type.</param>
        public Attribute(string name, Element owner, object value)
            : this()
        {
            if (name == null)
                throw new ArgumentNullException("name");

            Name = name;
            _Owner = owner;
            Value = value;
        }

        /// <summary>
        /// Creates a new Attribute with deferred loading.
        /// </summary>
        /// <param name="name">The name of the Attribute, which must be unique to its owner.</param>
        /// <param name="owner">The Element which owns this Attribute.</param>
        /// <param name="defer_offset">The location in the encoded DMX stream at which this Attribute's value can be found.</param>
        public Attribute(string name, Element owner, long defer_offset)
            : this(name, owner, null)
        {
            if (owner == null)
                throw new ArgumentNullException("owner");

            Offset = defer_offset;
        }

        #region Properties
        /// <summary>
        /// Gets or sets the name of this Attribute.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets the Type of this Attribute's Value.
        /// </summary>
        public Type ValueType { get; private set; }

        /// <summary>
        /// Gets the <see cref="Element"/> which this Attribute is part of.
        /// </summary>
        public Element Owner
        {
            get { return _Owner; }
            internal set
            {
                if (_Owner == value) return;

                if (Deferred && _Owner != null) DeferredLoad();
                _Owner = value;
            }
        }
        Element _Owner;

        Datamodel OwnerDatamodel { get { return Owner != null ? Owner.Owner : null; } }

        /// <summary>
        /// Gets whether the value of this Attribute has yet to be decoded.
        /// </summary>
        public bool Deferred { get { return Offset != 0; } }

        /// <summary>
        /// Loads the value of this Attribute from the encoded source Datamodel.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the Attribute has already been loaded.</exception>
        /// <exception cref="CodecException">Thrown when the deferred load fails.</exception>
        public void DeferredLoad()
        {
            if (Offset == 0) throw new InvalidOperationException("Attribute already loaded.");

            if (OwnerDatamodel == null || OwnerDatamodel.Codec == null)
                throw new CodecException("Trying to load a deferred Attribute, but could not find codec.");

            try
            {
                lock (OwnerDatamodel.Codec)
                {
                    _Value = OwnerDatamodel.Codec.DeferredDecodeAttribute(OwnerDatamodel, Offset);
                }
            }
            catch (Exception err)
            {
                throw new CodecException(String.Format("Deferred loading of attribute \"{0}\" on element {1} using codec {2} threw an exception.", Name, Owner.ID, OwnerDatamodel.Codec), err);
            }
            Offset = 0;

            var elem_array = _Value as ElementArray;
            if (elem_array != null)
                elem_array.Owner = Owner;
        }

        /// <summary>
        /// Gets or sets the value held by this Attribute.
        /// </summary>
        /// <exception cref="CodecException">Thrown when deferred value loading fails.</exception>
        /// <exception cref="DestubException">Thrown when Element destubbing fails.</exception>
        public object Value
        {
            get
            {
                if (Offset > 0)
                    DeferredLoad();

                if (OwnerDatamodel != null)
                {
                    // expand stubs
                    var elem = _Value as Element;
                    if (elem != null && elem.Stub)
                    {
                        try { _Value = OwnerDatamodel.OnStubRequest(elem.ID) ?? _Value; }
                        catch (Exception err) { throw new DestubException(this, err); }
                    }
                }

                return _Value;
            }
            set
            {
                ValueType = value == null ? typeof(Element) : value.GetType();

                if (!Datamodel.IsDatamodelType(ValueType))
                    throw new AttributeTypeException(String.Format("{0} is not a valid Datamodel attribute type. (If this is an array, it must implement IList<T>).", ValueType.FullName));

                var elem = value as Element;
                if (elem != null)
                {
                    if (elem.Owner == null)
                        elem.Owner = OwnerDatamodel;
                    else if (elem.Owner != OwnerDatamodel)
                        throw new ElementOwnershipException();
                }

                var elem_enumerable = value as IEnumerable<Element>;
                if (elem_enumerable != null)
                {
                    if (!(elem_enumerable is ElementArray))
                        throw new InvalidOperationException("Element array objects must derive from Datamodel.ElementArray");

                    var elem_array = (ElementArray)value;
                    if (elem_array.Owner == null)
                        elem_array.Owner = Owner;
                    else if (elem_array.Owner != Owner)
                        throw new InvalidOperationException("ElementArray is already owned by a different Datamodel.");

                    foreach (var arr_elem in elem_array)
                    {
                        if (arr_elem == null) continue;
                        else if (arr_elem.Owner == null)
                            arr_elem.Owner = OwnerDatamodel;
                        else if (arr_elem.Owner != OwnerDatamodel)
                            throw new ElementOwnershipException("One or more Elements in the assigned collection are from a different Datamodel. Use ImportElement() to copy them to this one before assigning.");
                    }
                }

                _Value = value;
                Offset = 0;
            }
        }
        object _Value;

        /// <summary>
        /// Gets the Attribute's Value without attempting deferred loading or destubbing.
        /// </summary>
        public object RawValue { get { return _Value; } }

        #endregion

        long Offset;

        public override string ToString()
        {
            var type = Value != null ? Value.GetType() : typeof(Element);
            var inner_type = Datamodel.GetArrayInnerType(type);
            return String.Format("{0} <{1}>", Name, inner_type != null ? inner_type.FullName + "[]" : type.FullName);
        }

        public AttrKVP ToKeyValuePair()
        {
            return new AttrKVP(Name, Value);
        }        
    }

    /// <summary>
    /// Compares two Attribute values, using <see cref="Element.IDComparer"/> for Element comparisons.
    /// </summary>
    public class ValueComparer : IEqualityComparer
    {
        /// <summary>
        /// Gets a default Attribute value equality comparer.
        /// </summary>
        public static ValueComparer Default
        {
            get
            {
                if (_Default == null)
                    _Default = new ValueComparer();
                return _Default;
            }
        }
        static ValueComparer _Default;

        public new bool Equals(object x, object y)
        {
            var type_x = x == null ? null : x.GetType();
            var type_y = y == null ? null : y.GetType();

            if (type_x == null && type_y == null)
                return true;

            if (type_x != type_y)
                return false;

            var inner = Datamodel.GetArrayInnerType(type_x);
            if (inner != null)
            {
                var array_left = (IList)x;
                var array_right = (IList)y;

                if (array_left.Count != array_right.Count) return false;
                
                return !Enumerable.Range(0, array_left.Count).Any(i => !Equals(array_left[i], array_right[i]));
            }
            else if (type_x == typeof(Element))
                return Element.IDComparer.Default.Equals((Element)x, (Element)y);
            else
                return EqualityComparer<object>.Default.Equals(x, y);
        }

        public int GetHashCode(object obj)
        {
            var elem = obj as Element;
            if (elem != null)
                return elem.ID.GetHashCode();

            var inner = Datamodel.GetArrayInnerType(obj.GetType());
            if (inner != null)
            {
                int hash = 0;
                foreach (var item in (IList)obj)
                    hash ^= item.GetHashCode();
                return hash;
            }

            return obj.GetHashCode();
        }
    }

    /// <summary>
    /// Wraps around an <see cref="Element"/> attribute or array item. Provides notifications of changes to the wrapped value(s) and provides an enumeration of ObservableAttribute children.
    /// </summary>
    /// <remarks>This type is provided as a utility. You must invoke it yourself, probably via a <see cref="System.Windows.Data.IValueConverter"/> implementation.</remarks>
    public class ObservableAttribute : INotifyPropertyChanged, INotifyCollectionChanged, IEnumerable
    {
        /*
        public class WrapAttributesConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var elem = (Element)value;
                return new ObservableAttribute(elem, null, elem);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return ((ObservableAttribute)value).Value;
            }
        }
        */

        /// <summary>
        /// Gets the attribute's name, or a generated label for an array item.
        /// </summary>
        public string Key
        {
            get
            {
                return _Key ?? String.Format("[{0}]", Index);
            }
            private set
            {
                _Key = value;
                OnPropertyChanged("Key");
            }
        }
        string _Key;

        /// <summary>
        /// Gets the attribute's value, or the array item.
        /// </summary>
        public object Value
        {
            get { return _Value; }
            private set
            {
                var incc = _Value as INotifyCollectionChanged;
                if (incc != null) incc.CollectionChanged -= OnValueCollectionChanged;

                _Value = value;
                OnPropertyChanged("Value");
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                WrappedAttributeMap.Clear();

                incc = _Value as INotifyCollectionChanged;
                if (incc != null) incc.CollectionChanged += OnValueCollectionChanged;
            }
        }
        object _Value;

        /// <summary>
        /// Gets the Element which holds the attribute or source array.
        /// </summary>
        public Element Owner { get; private set; }

        /// <summary>
        /// Gets the index of the attribute or array item.
        /// </summary>
        public int Index
        {
            get { return _Index; }
            private set { _Index = value; OnPropertyChanged("Index", "Key"); }
        }
        int _Index;

        protected Dictionary<AttrKVP, ObservableAttribute> WrappedAttributeMap { get; private set; }

        private ObservableAttribute(Element owner)
        {
            Owner = owner;
            WrappedAttributeMap = new Dictionary<AttrKVP, ObservableAttribute>();
        }

        /// <summary>
        /// Creates a new ObservableAttribute which represents an attribute of the given Element.
        /// </summary>
        public ObservableAttribute(Element owner, AttrKVP attribute)
            : this(owner)
        {
            Key = attribute.Key;
            Value = attribute.Value;
        }

        /// <summary>
        /// Creates a new ObservableAttribute which represents a fake attribute.
        /// </summary>
        public ObservableAttribute(Element owner, string key, object value)
            : this(owner)
        {
            Key = key;
            Value = value;
        }

        static ObservableAttribute()
        {
            ArrayExpansion = ArrayExpandMode.AllArrays;
        }

        protected IEnumerable<ObservableAttribute> WrapEnumerable(IEnumerable list, int starting_index = 0)
        {
            if (list == null) yield break;
            int i = 0;
            foreach (var source_item in list)
            {
                var output_attr = source_item is AttrKVP ? (AttrKVP)source_item : new AttrKVP(null, source_item);
                ObservableAttribute output_item = null;
                if (source_item != null && !WrappedAttributeMap.TryGetValue(output_attr, out output_item))
                {
                    var elem = source_item as Element;
                    output_item = WrappedAttributeMap[output_attr] = new ObservableAttribute(Owner, output_attr) { Index = starting_index + i };
                }
                yield return output_item;
                i++;
            }
        }

        private void OnValueCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            NotifyCollectionChangedEventArgs wrapped_event;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    wrapped_event = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, WrapEnumerable(e.NewItems, e.NewStartingIndex).ToArray());
                    break;
                case NotifyCollectionChangedAction.Move:
                    wrapped_event = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, WrapEnumerable(e.OldItems).ToArray(), e.NewStartingIndex, e.OldStartingIndex);
                    for (int i = 0; i < e.OldItems.Count; i++)
                        ((ObservableAttribute)e.OldItems[i]).Index = e.OldStartingIndex + i;
                    break;
                case NotifyCollectionChangedAction.Remove:
                    wrapped_event = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, WrapEnumerable(e.OldItems).ToArray(), e.OldStartingIndex);
                    foreach (var item in e.OldItems) WrappedAttributeMap.Remove((AttrKVP)item);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    wrapped_event = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, WrapEnumerable(e.NewItems).ToArray(), WrapEnumerable(e.OldItems).ToArray(), e.NewStartingIndex);
                    foreach (var item in e.OldItems)
                    {
                        if (!e.NewItems.Contains(item))
                            WrappedAttributeMap.Remove((AttrKVP)item);
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    wrapped_event = e;
                    WrappedAttributeMap.Clear();
                    break;
                default:
                    throw new NotImplementedException();
            }

            OnCollectionChanged(wrapped_event);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(params string[] property_names)
        {
            if (PropertyChanged != null)
                foreach (var prop_name in property_names)
                    PropertyChanged(this, new PropertyChangedEventArgs(prop_name));
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (CollectionChanged != null)
                CollectionChanged(this, e);
        }

        public enum ArrayExpandMode
        {
            /// <summary>
            /// Enumerate the children of all arrays.
            /// </summary>
            AllArrays,
            /// <summary>
            /// Enumerate only the children of Element arrays.
            /// </summary>
            ElementArrays,
        }

        /// <summary>
        /// Gets a value which determines the array types which expose their children.
        /// </summary>
        /// <remarks>Default value is AllArrays.</remarks>
        public static ArrayExpandMode ArrayExpansion { get; set; }

        public IEnumerator GetEnumerator()
        {
            if (Value == null) yield break;

            if (Value is Element || Value is ElementArray || (Datamodel.IsDatamodelArrayType(Value.GetType()) && ArrayExpansion == ArrayExpandMode.AllArrays))
                foreach (var item in WrapEnumerable((IEnumerable)Value))
                    yield return item;
        }
    }

    [Serializable]
    [TypeConverter(typeof(TypeConverters.Vector2Converter))]
    public struct Vector2 : IEnumerable<float>, IEquatable<Vector2>
    {
        public float X { get; set; }
        public float Y { get; set; }

        public static readonly Vector2 Zero = new Vector2();

        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y); } }

        public Vector2(float x, float y)
            : this()
        {
            X = x;
            Y = y;
        }
        public Vector2(IEnumerable<float> values)
            : this()
        {
            int i = 0;
            foreach (var ordinate in values.Take(2))
            {
                switch (i)
                {
                    case 0: X = ordinate; break;
                    case 1: Y = ordinate; break;
                }
                i++;
            }
        }

        public void Normalise()
        {
            var scale = 1 / Length;
            X *= scale;
            Y *= scale;
        }

        public double Dot(Vector2 other)
        {
            return X * other.X + Y * other.Y;
        }

        public IEnumerator<float> GetEnumerator()
        {
            yield return X;
            yield return Y;
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return String.Join(" ", X, Y);
        }

        public bool Equals(Vector2 other)
        {
            return X == other.X && Y == other.Y;
        }
        public override bool Equals(object obj)
        {
            if (!(obj is Vector2)) return false;
            return Equals((Vector2)obj);
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode();
        }

        public static bool operator ==(Vector2 a, Vector2 b)
        {
            return a.Equals(b);
        }
        public static bool operator !=(Vector2 a, Vector2 b)
        {
            return !a.Equals(b);
        }

        public static Vector2 operator -(Vector2 a, Vector2 b)
        {
            return new Vector2(a.X - b.X, a.Y - b.Y);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b)
        {
            return new Vector2(a.X + b.X, a.Y + b.Y);
        }

        public static Vector2 operator *(Vector2 a, float b)
        {
            return new Vector2(a.X * b, a.Y * b);
        }

        public static Vector2 operator /(Vector2 a, float b)
        {
            return new Vector2(a.X / b, a.Y / b);
        }
    }

    [Serializable]
    [TypeConverter(typeof(TypeConverters.Vector3Converter))]
    public struct Vector3 : IEnumerable<float>, IEquatable<Vector3>
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public static readonly Vector3 Zero = new Vector3();

        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }

        public Vector3(float x, float y, float z)
            : this()
        {
            X = x;
            Y = y;
            Z = z;
        }
        public Vector3(IEnumerable<float> values)
            : this()
        {
            int i = 0;
            foreach (var ordinate in values.Take(3))
            {
                switch (i)
                {
                    case 0: X = ordinate; break;
                    case 1: Y = ordinate; break;
                    case 2: Z = ordinate; break;
                }
                i++;
            }
        }

        public void Normalise()
        {
            var scale = 1 / Length;
            X *= scale;
            Y *= scale;
            Z *= scale;
        }

        public double Dot(Vector3 other)
        {
            return X * other.X + Y * other.Y + Z * other.Z;
        }

        public IEnumerator<float> GetEnumerator()
        {
            yield return X;
            yield return Y;
            yield return Z;
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return String.Join(" ", X, Y, Z);
        }

        public bool Equals(Vector3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }
        public override bool Equals(object obj)
        {
            if (!(obj is Vector3)) return false;
            return Equals((Vector3)obj);
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode();
        }

        public static bool operator ==(Vector3 a, Vector3 b)
        {
            return a.Equals(b);
        }
        public static bool operator !=(Vector3 a, Vector3 b)
        {
            return !a.Equals(b);
        }

        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Vector3 operator +(Vector3 a, Vector3 b)
        {
            return new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Vector3 operator *(Vector3 a, float b)
        {
            return new Vector3(a.X * b, a.Y * b, a.Z * b);
        }

        public static Vector3 operator /(Vector3 a, float b)
        {
            return new Vector3(a.X / b, a.Y / b, a.Z / b);
        }
    }

    [Serializable]
    [TypeConverter(typeof(TypeConverters.Vector3Converter))]
    public struct Angle : IEnumerable<float>, IEquatable<Angle>
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public static readonly Angle Zero = new Angle();

        public Angle(float x, float y, float z)
            : this()
        { X = x; Y = y; Z = z; }

        public Angle(IEnumerable<float> values)
            : this()
        {
            int i = 0;
            foreach (var ordinate in values.Take(3))
            {
                switch (i)
                {
                    case 0: X = ordinate; break;
                    case 1: Y = ordinate; break;
                    case 2: Z = ordinate; break;
                }
                i++;
            }
        }

        public IEnumerator<float> GetEnumerator()
        {
            yield return X;
            yield return Y;
            yield return Z;
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return String.Join(" ", X, Y, Z);
        }

        public bool Equals(Angle other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }
        public override bool Equals(object obj)
        {
            if (!(obj is Angle)) return false;
            return Equals((Angle)obj);
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode();
        }

        public static bool operator ==(Angle a, Angle b)
        {
            return a.Equals(b);
        }
        public static bool operator !=(Angle a, Angle b)
        {
            return !a.Equals(b);
        }

        public static Angle operator -(Angle a, Angle b)
        {
            return new Angle(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Angle operator +(Angle a, Angle b)
        {
            return new Angle(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Angle operator *(Angle a, float b)
        {
            return new Angle(a.X * b, a.Y * b, a.Z * b);
        }

        public static Angle operator /(Angle a, float b)
        {
            return new Angle(a.X / b, a.Y / b, a.Z / b);
        }
    }

    [Serializable]
    [TypeConverter(typeof(TypeConverters.Vector4Converter))]
    public struct Vector4 : IEnumerable<float>, IEquatable<Vector4>
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }

        public static readonly Vector4 Zero = new Vector4();

        public Vector4(float x, float y, float z, float w)
            : this()
        {
            X = x; Y = y; Z = z; W = w;
        }

        public Vector4(IEnumerable<float> values)
            : this()
        {
            int i = 0;
            foreach (var ordinate in values.Take(4))
            {
                switch (i)
                {
                    case 0: X = ordinate; break;
                    case 1: Y = ordinate; break;
                    case 2: Z = ordinate; break;
                    case 3: W = ordinate; break;
                }
                i++;
            }
        }

        public IEnumerator<float> GetEnumerator()
        {
            yield return X;
            yield return Y;
            yield return Z;
            yield return W;
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return String.Join(" ", X, Y, Z, W);
        }

        public bool Equals(Vector4 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        }
        public override bool Equals(object obj)
        {
            if (!(obj is Vector4)) return false;
            return Equals((Vector4)obj);
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode() ^ W.GetHashCode();
        }

        public static bool operator ==(Vector4 a, Vector4 b)
        {
            return a.Equals(b);
        }
        public static bool operator !=(Vector4 a, Vector4 b)
        {
            return !a.Equals(b);
        }

        public static Vector4 operator -(Vector4 a, Vector4 b)
        {
            return new Vector4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        }

        public static Vector4 operator +(Vector4 a, Vector4 b)
        {
            return new Vector4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        }

        public static Vector4 operator *(Vector4 a, float b)
        {
            return new Vector4(a.X * b, a.Y * b, a.Z * b, a.W * b);
        }

        public static Vector4 operator /(Vector4 a, float b)
        {
            return new Vector4(a.X / b, a.Y / b, a.Z / b, a.W / b);
        }
    }

    [Serializable]
    [TypeConverter(typeof(TypeConverters.Vector4Converter))]
    public struct Quaternion : IEnumerable<float>
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }

        public static readonly Quaternion Zero = new Quaternion();

        public Quaternion(float x, float y, float z, float w)
            : this()
        {
            X = x; Y = y; Z = z; W = w;
        }

        public Quaternion(IEnumerable<float> values)
            : this()
        {
            int i = 0;
            foreach (var ordinate in values.Take(4))
            {
                switch (i)
                {
                    case 0: X = ordinate; break;
                    case 1: Y = ordinate; break;
                    case 2: Z = ordinate; break;
                    case 3: W = ordinate; break;
                }
                i++;
            }
        }

        public void Normalise()
        {
            float scale = 1.0f / (float)(System.Math.Sqrt(W * W + (X * X + Y * Y + Z * Z)));
            X *= scale;
            Y *= scale;
            Z *= scale;
            W *= scale;
        }

        public IEnumerator<float> GetEnumerator()
        {
            yield return X;
            yield return Y;
            yield return Z;
            yield return W;
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return String.Join(" ", X, Y, Z, W);
        }

        public bool Equals(Quaternion other)
        {
            return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        }
        public override bool Equals(object obj)
        {
            if (!(obj is Quaternion)) return false;
            return Equals((Quaternion)obj);
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode() ^ W.GetHashCode();
        }

        public static bool operator ==(Quaternion a, Quaternion b)
        {
            return a.Equals(b);
        }
        public static bool operator !=(Quaternion a, Quaternion b)
        {
            return !a.Equals(b);
        }
    }

    [Serializable]
    [TypeConverter(typeof(TypeConverters.MatrixConverter))]
    public struct Matrix : IEnumerable<float>, IEquatable<Matrix>
    {
        public Vector4 Row0 { get; set; }
        public Vector4 Row1 { get; set; }
        public Vector4 Row2 { get; set; }
        public Vector4 Row3 { get; set; }

        public static readonly Matrix Zero = new Matrix();

        public Matrix(float[,] value)
            : this()
        {
            if (value.GetUpperBound(0) < 4)
                throw new InvalidOperationException("Not enough columns for a Matrix4.");

            Row0 = new Vector4(value.GetValue(0) as float[]);
            Row1 = new Vector4(value.GetValue(1) as float[]);
            Row2 = new Vector4(value.GetValue(2) as float[]);
            Row3 = new Vector4(value.GetValue(3) as float[]);
        }

        public Matrix(IEnumerable<float> values)
            : this()
        {
            if (values.Count() < 4 * 4)
                throw new ArgumentException("Not enough values for a Matrix4.");

            Row0 = new Vector4(values.Take(4));
            Row1 = new Vector4(values.Skip(4).Take(4));
            Row2 = new Vector4(values.Skip(8).Take(4));
            Row3 = new Vector4(values.Skip(12).Take(4));
        }

        public override string ToString()
        {
            return String.Join("  ", Row0, Row1, Row2, Row3);
        }

        public IEnumerator<float> GetEnumerator()
        {
            foreach (var value in Row0)
                yield return value;
            foreach (var value in Row1)
                yield return value;
            foreach (var value in Row2)
                yield return value;
            foreach (var value in Row3)
                yield return value;
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool Equals(Matrix other)
        {
            return Row0.Equals(other.Row0) && Row1.Equals(other.Row1) && Row2.Equals(other.Row2) && Row3.Equals(other.Row3);
        }
        public override bool Equals(object obj)
        {
            if (!(obj is Matrix)) return false;
            return Equals((Matrix)obj);
        }

        public override int GetHashCode()
        {
            return Row0.GetHashCode() ^ Row1.GetHashCode() ^ Row2.GetHashCode() ^ Row3.GetHashCode();
        }

        public static bool operator ==(Matrix a, Matrix b)
        {
            return a.Equals(b);
        }
        public static bool operator !=(Matrix a, Matrix b)
        {
            return !a.Equals(b);
        }
    }

    namespace TypeConverters
    {
        public abstract class VectorConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                if (sourceType == typeof(string)) return true;
                return base.CanConvertFrom(context, sourceType);
            }

            protected float[] GetOrdinates(string value, CultureInfo culture)
            {
                return value.Split(new string[] { culture.NumberFormat.CurrencyGroupSeparator, " " }, StringSplitOptions.RemoveEmptyEntries).Select(s => Single.Parse(s)).ToArray();
            }

            protected Exception NotEnoughValues(int expected, int actual)
            {
                return new ArgumentException(String.Format("Expected {0} values, got {1}", expected, actual));
            }
        }

        public class Vector2Converter : VectorConverter
        {
            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                var str_val = value as string;
                if (str_val != null)
                {
                    var ordinates = GetOrdinates(str_val, culture);
                    if (ordinates.Length != 2) throw NotEnoughValues(2, ordinates.Length);

                    return new Vector2(ordinates[0], ordinates[1]);
                }
                return base.ConvertFrom(context, culture, value);
            }
        }

        public class Vector3Converter : VectorConverter
        {
            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                var str_val = value as string;
                if (str_val != null)
                {
                    var ordinates = GetOrdinates(str_val, culture);
                    if (ordinates.Length < 3) throw NotEnoughValues(3, ordinates.Length);

                    return new Vector3(ordinates[0], ordinates[1], ordinates[2]);
                }
                return base.ConvertFrom(context, culture, value);
            }
        }

        public class Vector4Converter : VectorConverter
        {
            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                var str_val = value as string;
                if (str_val != null)
                {
                    var ordinates = GetOrdinates(str_val, culture);
                    if (ordinates.Length < 4) throw NotEnoughValues(4, ordinates.Length);

                    return new Vector4(ordinates[0], ordinates[1], ordinates[2], ordinates[3]);
                }
                return base.ConvertFrom(context, culture, value);
            }
        }

        public class MatrixConverter : VectorConverter
        {
            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                var str_val = value as string;
                if (str_val != null)
                {
                    var ordinates = GetOrdinates(str_val, culture);
                    if (ordinates.Length < 4 * 4) throw NotEnoughValues(4 * 4, ordinates.Length);

                    return new Matrix(ordinates);
                }
                return base.ConvertFrom(context, culture, value);
            }
        }
    }
}
