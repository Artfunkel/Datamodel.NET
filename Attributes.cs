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
            _Value = _ValueType = null; // dummy set to keep the compiler happy; the real, validated one comes at the end of the constructor
            Offset = 0;
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
        public string Name;

        public Type ValueType { get { return _ValueType; } }
        Type _ValueType;

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
                var owner_dm = OwnerDatamodel;
                var elem = value as Element;
                if (elem != null)
                {
                    if (elem.Owner == null)
                        elem.Owner = owner_dm;
                    else if (elem.Owner != owner_dm)
                        throw new ElementOwnershipException();
                }
                var elem_list = value as IEnumerable<Element>;
                if (elem_list != null)
                foreach(var arr_elem in elem_list)
                {
                    if (arr_elem == null) continue;
                    else if (arr_elem.Owner == null)
                        arr_elem.Owner = owner_dm;
                    else if (arr_elem.Owner != owner_dm)
                        throw new ElementOwnershipException("One or more Elements in the assigned collection are from a different Datamodel. Use ImportElement() to copy them to this one before assigning.");
                }

                _Value = value;
                Offset = 0;
                _ValueType = value == null ? typeof(Element) : value.GetType();
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

    [Serializable]
    public struct Vector2 : IEnumerable<float>, IEquatable<Vector2>
    {
        public float X;
        public float Y;

        public static readonly Vector2 Zero = new Vector2();

        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y); } }

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }
        public Vector2(IEnumerable<float> values)
            : this()
        {
            X = Y = 0;
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
    public struct Vector3 : IEnumerable<float>, IEquatable<Vector3>
    {
        public float X;
        public float Y;
        public float Z;

        public static readonly Vector3 Zero = new Vector3();

        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        public Vector3(IEnumerable<float> values)
        {
            X = Y = Z = 0;
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
    public struct Angle : IEnumerable<float>, IEquatable<Angle>
    {
        public float X;
        public float Y;
        public float Z;

        public static readonly Angle Zero = new Angle();

        public Angle(float x, float y, float z)
        { X = x; Y = y; Z = z; }

        public Angle(IEnumerable<float> values)
        {
            X = Y = Z = 0;
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
    public struct Vector4 : IEnumerable<float>, IEquatable<Vector4>
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public static readonly Vector4 Zero = new Vector4();

        public Vector4(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        public Vector4(IEnumerable<float> values)
        {
            X = Y = Z = W = 0;
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
    public struct Quaternion : IEnumerable<float>
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public static readonly Quaternion Zero = new Quaternion();

        public Quaternion(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        public Quaternion(IEnumerable<float> values)
        {
            X = Y = Z = W = 0;

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
    public struct Matrix : IEnumerable<float>, IEquatable<Matrix>
    {
        public Vector4 Row0;
        public Vector4 Row1;
        public Vector4 Row2;
        public Vector4 Row3;

        public static readonly Matrix Zero = new Matrix();

        public Matrix(float[,] value)
        {
            if (value.GetUpperBound(0) < 4)
                throw new InvalidOperationException("Not enough columns for a Matrix4.");

            Row0 = new Vector4(value.GetValue(0) as float[]);
            Row1 = new Vector4(value.GetValue(1) as float[]);
            Row2 = new Vector4(value.GetValue(2) as float[]);
            Row3 = new Vector4(value.GetValue(3) as float[]);
        }

        public Matrix(IEnumerable<float> values)
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
}
