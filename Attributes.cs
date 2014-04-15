using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

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
        {
            if (name == null)
                throw new ArgumentNullException("name");

            Name = name;
            _Value = value;
            ValueType = value != null ? value.GetType() : typeof(Element);
            Offset = 0;
            _Owner = owner;
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
        public string Name;

        public Type ValueType;

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
        }

        /// <summary>
        /// Gets or sets the value held by this Attribute.
        /// </summary>
        /// <exception cref="CodecException">Thrown when deferred value loading fails.</exception>
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
                        _Value = OwnerDatamodel.OnStubRequest(elem.ID) ?? _Value;
                    var elem_list = _Value as IList<Element>;
                    if (elem_list != null)
                        for (int i = 0; i < elem_list.Count; i++)
                        {
                            if (elem_list[i] == null || !elem_list[i].Stub) continue;
                            elem_list[i] = OwnerDatamodel.OnStubRequest(elem_list[i].ID) ?? elem_list[i];
                        }
                }

                return _Value;
            }
            set
            {
                _Value = value;
                Offset = 0;
            }
        }
        object _Value;
        #endregion

        long Offset;

        public override string ToString()
        {
            var type = Value != null ? Value.GetType() : typeof(Element);
            var inner_type = Datamodel.GetArrayInnerType(type);
            return String.Format("{0} <{1}>", Name, inner_type != null ? inner_type.FullName + "[]" : type.FullName);
        }

        /// <summary>
        /// Compares two Attributes by their values.
        /// </summary>
        public class ValueComparer : IEqualityComparer, IEqualityComparer<Attribute>
        {
            /// <summary>
            /// Gets a default Attribute Value equality comparer.
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

            public bool Equals(Attribute x, Attribute y)
            {
                var type_x = x.Value == null ? null : x.Value.GetType();
                var type_y = y.Value == null ? null : y.Value.GetType();

                if (type_x == null && type_y == null)
                    return true;

                if (type_x != type_y)
                    return false;

                var inner = Datamodel.GetArrayInnerType(type_x);
                if (inner != null)
                {
                    var array_left = (IList)x.Value;
                    var array_right = (IList)y.Value;

                    if (array_left.Count != array_right.Count) return false;

                    var comparer = inner == typeof(Element) ? (IEqualityComparer)Element.IDComparer.Default : EqualityComparer<object>.Default;

                    return !Enumerable.Range(0, array_left.Count).Any(i => !comparer.Equals(array_left[i], array_right[i]));
                }
                else if (type_x == typeof(Element))
                    return Element.IDComparer.Default.Equals((Element)x.Value, (Element)y.Value);
                else
                    return EqualityComparer<object>.Default.Equals(x.Value, y.Value);
            }

            public int GetHashCode(Attribute obj)
            {
                return obj.Value.GetHashCode();
            }

            bool IEqualityComparer.Equals(object x, object y)
            {
                return Equals((Attribute)x, (Attribute)y);
            }

            int IEqualityComparer.GetHashCode(object obj)
            {
                return GetHashCode((Attribute)obj);
            }
        }

    }

    public abstract class VectorBase : IEnumerable<float>, INotifyPropertyChanged
    {
        /// <summary>
        /// Gets the number of ordinates in this vector.
        /// </summary>
        public abstract int Size { get; }

        /// <summary>
        /// Raised when an ordinate changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }

        public abstract IEnumerator<float> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return String.Join(" ", this.ToArray());
        }

        public override bool Equals(object obj)
        {
            var vec = obj as VectorBase;
            if (vec == null) return false;

            if (vec.Size != this.Size) return false;

            var this_e = this.GetEnumerator();
            var vec_e = vec.GetEnumerator();

            for (int i = 0; i < this.Size; i++)
            {
                this_e.MoveNext();
                vec_e.MoveNext();
                if (this_e.Current != vec_e.Current) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            int hash = 0;
            foreach (var ord in this)
                hash ^= ord.GetHashCode();
            return hash;
        }
    }

    public class Vector2 : VectorBase
    {
        public override int Size { get { return 2; } }

        public float X { get { return x; } set { x = value; NotifyPropertyChanged("X"); } }
        public float Y { get { return y; } set { y = value; NotifyPropertyChanged("Y"); } }

        float x;
        float y;

        public Vector2()
        { }

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
        public Vector2(IEnumerable<float> values)
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

        public override IEnumerator<float> GetEnumerator()
        {
            yield return X;
            yield return Y;
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

    public class Vector3 : Vector2
    {
        public override int Size { get { return 3; } }

        public float Z { get { return z; } set { z = value; NotifyPropertyChanged("Z"); } }
        float z;

        public Vector3()
        { }

        public Vector3(float x, float y, float z)
            : base(x, y)
        {
            this.z = z;
        }
        public Vector3(IEnumerable<float> values)
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

        public override IEnumerator<float> GetEnumerator()
        {
            yield return X;
            yield return Y;
            yield return Z;
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

    public class Angle : Vector3
    {
        public Angle()
            : base()
        { }

        public Angle(float x, float y, float z)
            : base(x, y, z)
        { }
        public Angle(IEnumerable<float> values)
            : base(values)
        { }

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

    public class Vector4 : Vector3
    {
        public override int Size { get { return 4; } }

        public float W { get { return w; } set { w = value; NotifyPropertyChanged("W"); } }
        float w;

        public Vector4()
        {
        }

        public Vector4(float x, float y, float z, float w)
            : base(x, y, z)
        {
            this.w = w;
        }
        public Vector4(IEnumerable<float> values)
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

        public override IEnumerator<float> GetEnumerator()
        {
            yield return X;
            yield return Y;
            yield return Z;
            yield return W;
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

    public class Quaternion : Vector4
    {
        public Quaternion()
            : base()
        { }

        public Quaternion(float x, float y, float z, float w)
            : base(x, y, z, w)
        { }
        public Quaternion(IEnumerable<float> values)
            : base(values)
        { }
    }

    public class Matrix : VectorBase
    {
        public override int Size { get { return 4 * 4; } }

        public Vector4 Row0 { get { return row0; } set { row0 = value; NotifyPropertyChanged("Row0"); } }
        public Vector4 Row1 { get { return row1; } set { row1 = value; NotifyPropertyChanged("Row1"); } }
        public Vector4 Row2 { get { return row2; } set { row2 = value; NotifyPropertyChanged("Row2"); } }
        public Vector4 Row3 { get { return row3; } set { row3 = value; NotifyPropertyChanged("Row3"); } }

        Vector4 row0 = new Vector4();
        Vector4 row1 = new Vector4();
        Vector4 row2 = new Vector4();
        Vector4 row3 = new Vector4();

        public Matrix()
        { }

        public Matrix(float[,] value)
        {
            if (value.GetUpperBound(0) < 4)
                throw new InvalidOperationException("Not enough columns for a Matrix4.");

            row0 = new Vector4(value.GetValue(0) as float[]);
            row1 = new Vector4(value.GetValue(1) as float[]);
            row2 = new Vector4(value.GetValue(2) as float[]);
            row3 = new Vector4(value.GetValue(3) as float[]);
        }

        public Matrix(IEnumerable<float> values)
        {
            if (values.Count() < 4 * 4)
                throw new ArgumentException("Not enough values for a Matrix4.");

            row0 = new Vector4(values.Take(4));
            row1 = new Vector4(values.Skip(4).Take(4));
            row2 = new Vector4(values.Skip(8).Take(4));
            row3 = new Vector4(values.Skip(12).Take(4));
        }

        public override string ToString()
        {
            return String.Join("  ", Row0, Row1, Row2, Row3);
        }

        public override IEnumerator<float> GetEnumerator()
        {
            return Row0.Concat(Row1.Concat(Row2.Concat(Row3))).GetEnumerator();
        }
    }
}
