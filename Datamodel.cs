using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Security;
using System.Runtime.Serialization;

using Codec_t = System.Tuple<string, int>;
using Datamodel.Codecs;

namespace Datamodel
{
    /// <summary>
    /// Represents a thread-safe tree of <see cref="Element"/>s.
    /// </summary>
    public class Datamodel : INotifyPropertyChanged, IDisposable
    {
        #region Attribute types
        public static Type[] AttributeTypes { get { return _AttributeTypes; } }
        static Type[] _AttributeTypes = { typeof(Element), typeof(int), typeof(float), typeof(bool), typeof(string), typeof(byte[]), 
                typeof(TimeSpan), typeof(System.Drawing.Color), typeof(Vector2), typeof(Vector3),typeof(Vector4), typeof(Angle), typeof(Quaternion), typeof(Matrix) };

        /// <summary>
        /// Determines whether the given Type is valid as a Datamodel <see cref="Attribute"/>.
        /// </summary>
        /// <remarks><see cref="ICollection&lt;T&gt;"/> objects pass if their generic argument is valid.</remarks>
        /// <seealso cref="IsDatamodelArrayType"/>
        /// <param name="t">The Type to check.</param>
        public static bool IsDatamodelType(Type t)
        {
            return Datamodel.AttributeTypes.Contains(t) || IsDatamodelArrayType(t);
        }

        /// <summary>
        /// Determines whether the given Type is valid as a Datamodel <see cref="Attribute"/> array.
        /// </summary>
        /// <seealso cref="IsDatamodelType"/>
        /// <seealso cref="GetArrayInnerType"/>
        /// <param name="t">The Type to check.</param>
        public static bool IsDatamodelArrayType(Type t)
        {
            var inner = GetArrayInnerType(t);
            return inner != null ? Datamodel.AttributeTypes.Contains(inner) : false;
        }

        /// <summary>
        /// Returns the inner Type of an object which implements IList&lt;T&gt;, or null if there is no inner Type.
        /// </summary>
        /// <param name="t">The Type to check.</param>
        public static Type GetArrayInnerType(Type t)
        {
            var i_type = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>) ? t : t.GetInterface("IList`1");
            if (i_type == null) return null;

            var inner = i_type.GetGenericArguments()[0];
            return inner == typeof(byte) ? null : inner; // exception for byte[]
        }
        #endregion

        static Datamodel()
        {
            Datamodel.RegisterCodec(typeof(Codecs.Binary), "binary", 1, 2, 3, 4, 5);
            Datamodel.RegisterCodec(typeof(Codecs.KeyValues2), "keyvalues2", 1);
            TextEncoding = new System.Text.UTF8Encoding(false);
        }

        #region Codecs
        struct CodecRegistration
        {
            public CodecRegistration(string encoding, int version)
            {
                Encoding = encoding;
                Version = version;
            }

            public string Encoding;
            public int Version;
        }
        static Dictionary<CodecRegistration, Type> Codecs = new Dictionary<CodecRegistration, Type>();

        public static IEnumerable<Codec_t> CodecsRegistered { get { return Codecs.Select(t => new Codec_t(t.Key.Encoding, t.Key.Version)); } }

        /// <summary>
        /// Registers a new <see cref="ICodec"/> with an encoding name and one or more encoding versions.
        /// </summary>
        /// <remarks>Existing codecs will be replaced.</remarks>
        /// <param name="type">The ICodec implementation being registered.</param>
        /// <param name="encoding_name">The encoding name that the codec handles.</param>
        /// <param name="encoding_versions">The encoding version(s) that the codec handles.</param>
        public static void RegisterCodec(Type type, string encoding_name, params int[] encoding_versions)
        {
            if (type.GetInterface(typeof(ICodec).FullName) == null) throw new CodecException(String.Format("{0} does not implement Datamodel.ICodec.", type.Name));
            if (type.GetConstructor(Type.EmptyTypes) == null) throw new CodecException(String.Format("{0} does not have a default constructor.", type.Name));

            foreach (var version in encoding_versions)
            {
                var reg = new CodecRegistration(encoding_name, version);
                if (Codecs.ContainsKey(reg) && Codecs[reg] != type)
                    System.Diagnostics.Trace.TraceInformation("Datamodel.NET: Replacing existing codec for {0} {1} ({2}) with {3}", encoding_name, version, Codecs[reg].Name, type.Name);
                Codecs[reg] = type;
            }
        }

        static ICodec GetCodec(string encoding, int encoding_version)
        {
            Type codec_type;
            if (!Codecs.TryGetValue(new CodecRegistration(encoding, encoding_version), out codec_type))
                throw new CodecException(String.Format("No codec found for {0} version {1}.", encoding, encoding_version));

            return (ICodec)codec_type.GetConstructor(Type.EmptyTypes).Invoke(null);
        }

        /// <summary>
        /// Determines whether a codec has been registered for the given encoding name and version.
        /// </summary>
        /// <param name="encoding">The name of the encoding to check for.</param>
        /// <param name="encoding_version">The version of the encoding to check for.</param>
        public static bool HaveCodec(string encoding, int encoding_version)
        {
            return Codecs.ContainsKey(new CodecRegistration(encoding, encoding_version));
        }
        #endregion

        #region Save / Load

        /// <summary>
        /// Gets or sets the assumed encoding of text in DMX files. Defaults to UTF8.
        /// </summary>
        /// <remarks>Changing this value does not alter Datamodels whiche are already in memory.</remarks>
        public static System.Text.Encoding TextEncoding { get; set; }

        /// <summary>
        /// Writes this Datamodel to a <see cref="Stream"/> with the given encoding and encoding version.
        /// </summary>
        /// <param name="stream">The output Stream.</param>
        /// <param name="encoding">The desired encoding.</param>
        /// <param name="encoding_version">The desired encoding version.</param>
        public void Save(Stream stream, string encoding, int encoding_version)
        {
            GetCodec(encoding, encoding_version).Encode(this, encoding_version, stream);
        }

        /// <summary>
        /// Writes this Datamodel to a file path with the given encoding and encoding version.
        /// </summary>
        /// <param name="path">The destination file path.</param>
        /// <param name="encoding">The desired encoding.</param>
        /// <param name="encoding_version">The desired encoding version.</param>
        public void Save(string path, string encoding, int encoding_version)
        {
            using (var stream = System.IO.File.Create(path))
            {
                Save(stream, encoding, encoding_version);
            }
        }

        /// <summary>
        /// Loads a Datamodel from a <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The input Stream.</param>
        /// <param name="defer_mode">How to handle deferred loading.</param>
        public static Datamodel Load(Stream stream, DeferredMode defer_mode = DeferredMode.Automatic)
        {
            return Load_Internal(stream, defer_mode);
        }
        /// <summary>
        /// Loads a Datamodel from a byte array.
        /// </summary>
        /// <param name="stream">The input Stream.</param>
        /// <param name="defer_mode">How to handle deferred loading.</param>
        public static Datamodel Load(byte[] data, DeferredMode defer_mode = DeferredMode.Automatic)
        {
            return Load_Internal(new MemoryStream(data, true), defer_mode);
        }

        /// <summary>
        /// Loads a Datamodel from a file path.
        /// </summary>
        /// <param name="path">The source file path.</param>
        /// <param name="defer_mode">How to handle deferred loading.</param>
        public static Datamodel Load(string path, DeferredMode defer_mode = DeferredMode.Automatic)
        {
            var stream = System.IO.File.OpenRead(path);
            Datamodel dm = null;
            try
            {
                dm = Load_Internal(stream, defer_mode);
                return dm;
            }
            finally
            {
                if (defer_mode == DeferredMode.Disabled || (dm != null && dm.Codec == null)) stream.Dispose();
            }
        }

        static Datamodel Load_Internal(Stream stream, DeferredMode defer_mode = DeferredMode.Automatic)
        {
            stream.Seek(0, SeekOrigin.Begin);
            var header = String.Empty;
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                header += (char)b;
                if (b == '>') break;
                if (header.Length > 128) // probably not a DMX at this point
                    break;
            }

            var match = System.Text.RegularExpressions.Regex.Match(header, CodecUtilities.HeaderPattern_Regex);

            if (!match.Success || match.Groups.Count != 5)
                throw new InvalidOperationException(String.Format("Could not read DMX header ({0}).", header));

            string encoding = match.Groups[1].Value;
            int encoding_version = int.Parse(match.Groups[2].Value);

            string format = match.Groups[3].Value;
            int format_version = int.Parse(match.Groups[4].Value);

            ICodec codec = GetCodec(encoding, encoding_version);

            var dm = codec.Decode(encoding_version, format, format_version, stream, defer_mode);
            if (defer_mode == DeferredMode.Automatic && codec is IDeferredAttributeCodec)
            {
                dm.Stream = stream;
                dm.Codec = (IDeferredAttributeCodec)codec;
            }

            dm.Format = format;
            dm.FormatVersion = format_version;

            dm.Encoding = encoding;
            dm.EncodingVersion = encoding_version;

            return dm;
        }

        #endregion

        /// <summary>
        /// A collection of <see cref="Element"/>s owned by a single <see cref="Datamodel"/>.
        /// </summary>
        public class ElementList : IEnumerable<Element>, INotifyCollectionChanged
        {
            internal object ChangeLock = new object();

            List<Element> store = new List<Element>();
            Datamodel Owner;

            internal ElementList(Datamodel owner)
            {
                Owner = owner;
            }

            internal void Add(Element item)
            {
                lock (ChangeLock)
                {
                    if (item.Owner != null && item.Owner != Owner)
                        throw new InvalidOperationException("Cannot add an element from a different Datamodel. Use ImportElement() first.");
                    // if it's in the datamodel, its ID has already been checked for collisions.
                    store.Add(item);
                    if (CollectionChanged != null) CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
                }
            }

            /// <summary>
            /// Returns the <see cref="Element"/> at the specified index.
            /// </summary>
            /// <remarks>The order of this list has no meaning to a Datamodel. This accessor is intended for <see cref="ICodec"/> implementers.</remarks>
            /// <param name="index">The index to look up.</param>
            /// <returns>The Element found at the index.</returns>
            public Element this[int index] { get { return store[index]; } }

            /// <summary>
            /// Searches the collection for an <see cref="Element"/> with the specified <see cref="Element.ID"/>.
            /// </summary>
            /// <param name="id">The ID to search for.</param>
            /// <returns>The Element with the given ID, or null if none is found.</returns>
            public Element this[Guid id] { get { lock (ChangeLock) { return store.FirstOrDefault(e => e.ID == id); } } }

            /// <summary>
            /// Gets the number of <see cref="Element"/>s in this collection.
            /// </summary>
            public int Count { get { return store.Count; } }

            /// <summary>
            /// Specifies a behaviour for removing references to an <see cref="Element"/> from other Elements in a <see cref="Datamodel"/>.
            /// </summary>
            public enum RemoveMode
            {
                /// <summary>
                /// Attribute values pointing to the removed Element become stubs.
                /// </summary>
                MakeStubs,
                /// <summary>
                /// Attribute values pointing to the removed Element become null.
                /// </summary>
                MakeNulls
            }
            /// <summary>
            /// Removes an <see cref="Element"/> from the collection.
            /// </summary>
            /// <param name="item">The Element to remove</param>
            /// <param name="mode">The action to take if a reference to this Element is found other Elements.</param>
            /// <returns>true if item is successfully removed; otherwise, false.</returns>
            public bool Remove(Element item, RemoveMode mode)
            {
                lock (ChangeLock)
                {
                    if (store.Remove(item))
                    {
                        foreach (var attr in store.AsParallel().SelectMany(e => e.Where(a => a.Value == item)))
                            attr.Value = (mode == RemoveMode.MakeStubs) ? Owner.CreateStubElement(((Element)attr.Value).ID) : (Element)null;

                        if (Owner.Root == item) Owner.Root = null;

                        if (CollectionChanged != null) CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
                        return true;
                    }
                    else return false;
                }
            }

            #region Interfaces
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return store.GetEnumerator();
            }
            /// <summary>
            /// Returns an Enumerator that iterates through the Elements in collection.
            /// </summary>
            public IEnumerator<Element> GetEnumerator()
            {
                return store.GetEnumerator();
            }
            /// <summary>
            /// Raised when an <see cref="Element"/> is added, removed, or replaced.
            /// </summary>
            public event NotifyCollectionChangedEventHandler CollectionChanged;
            #endregion
        }

        /// <summary>
        /// Creates a new Datamodel.
        /// </summary>
        /// <param name="format">The format of the Datamodel. This is not the same as the encoding used to save or load the Datamodel.</param>
        /// <param name="format_version">The version of the format in use.</param>
        public Datamodel(string format, int format_version)
        {
            Format = format;
            FormatVersion = format_version;
            AllElements = new ElementList(this);
        }

        /// <summary>
        /// Releases any <see cref="Stream"/> being used to deferred load the Datamodel's <see cref="Attribute"/>s.
        /// </summary>
        public void Dispose()
        {
            if (Stream != null) Stream.Dispose();
        }

        #region Properties

        /// <summary>
        /// Gets or sets whether new Elements with random IDs can be created by this Datamodel.
        /// </summary>
        public bool AllowRandomIDs
        {
            get { return _AllowRandomIDs; }
            set { _AllowRandomIDs = value; NotifyPropertyChanged("AllowRandomIDs"); }
        }
        bool _AllowRandomIDs = true;

        /// <summary>
        /// Gets or sets the format of the Datamodel.
        /// </summary>
        public string Format
        {
            get { return _Format; }
            set
            {
                if (value != null && value.Contains(' '))
                    throw new ArgumentException("Format name cannot contain spaces.");
                _Format = value;
                NotifyPropertyChanged("Format");
            }
        }
        string _Format;

        /// <summary>
        /// Gets or sets the version of the <see cref="Format"/> in use.
        /// </summary>
        public int FormatVersion
        {
            get { return _FormatVersion; }
            set { _FormatVersion = value; NotifyPropertyChanged("FormatVersion"); }
        }
        int _FormatVersion;

        /// <summary>
        /// Gets or sets the encoding with which this Datamodel should be stored.
        /// </summary>
        public string Encoding
        {
            get { return _Encoding; }
            set { _Encoding = value; NotifyPropertyChanged("Encoding"); }
        }
        string _Encoding;

        /// <summary>
        /// Gets or sets the version of the <see cref="Encoding"/> in use.
        /// </summary>
        public int EncodingVersion
        {
            get { return _EncodingVersion; }
            set { _EncodingVersion = value; NotifyPropertyChanged("EncodingVersion"); }
        }
        int _EncodingVersion;

        Stream Stream;
        internal IDeferredAttributeCodec Codec;

        /// <summary>
        /// Gets or sets the first Element of the Datamodel. Only Elements referenced by the Root element or one of its children are considered a part of the Datamodel.
        /// </summary>
        /// <exception cref="ElementOwnershipException">Thown when an attempt is made to assign an Element from another Datamodel to this property.</exception>
        public Element Root
        {
            get { return _Root; }
            set
            {
                if (value != null && value.Owner != this)
                    throw new ElementOwnershipException("Cannot add an element from a different Datamodel. Use ImportElement() first.");
                _Root = value;
                NotifyPropertyChanged("Root");
            }
        }
        Element _Root;

        /// <summary>
        /// Gets all Elements owned by this Datamodel. Only Elements which are referenced by the Root element or one of its children are actually considered part of the Datamodel.
        /// </summary>
        public ElementList AllElements { get; protected set; }
        #endregion

        #region Element handling
        /// <summary>
        /// Copies another Datamodel's <see cref="Element"/> into this one.
        /// </summary>
        /// <remarks>An imported Element will automatically replace any stub Elements which share its ID.</remarks>
        /// <param name="foreign_element">The Element to import. Must be owned by a different Datamodel.</param>
        /// <param name="deep">Whether to import child Elements as well.</param>
        /// <param name="overwrite">If true, foreign Elements will replace local stub Elements which share their ID. If false, the same foregin Elements will be skipped.</param>
        /// <returns>A copy of the input Element owned by this Datamodel.</returns>
        /// <exception cref="ArgumentNullException">Thrown if foreign_element is null.</exception>
        /// <exception cref="ElementOwnershipException">Thrown if foreign_element is already owned by this Datamodel.</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown when the maximum number of Elements allowed in a Datamodel has been reached.</exception>
        /// <seealso cref="Element.Stub"/>
        /// <seealso cref="Element.ID"/>
        public Element ImportElement(Element foreign_element, bool deep, bool overwrite)
        {
            if (foreign_element == null) throw new ArgumentNullException("element");
            if (foreign_element.Owner == this) throw new ElementOwnershipException("Element is already a part of this Datamodel.");
            return ImportElement_internal(foreign_element, deep, overwrite);
        }

        object CopyValue(object value, bool deep, bool overwrite)
        {
            if (value == null) return null;
            var attr_type = value.GetType();

            // do nothing for a value type or string...
            if (attr_type.IsValueType || attr_type == typeof(string))
                return value;

            // ...copy a reference type
            else if (attr_type == typeof(Element))
                return deep ? ImportElement_internal((Element)value, true, overwrite) : CreateStubElement(((Element)value).ID);
            else if (attr_type == typeof(Vector2))
                return new Vector2((Vector2)value);
            else if (attr_type == typeof(Vector3))
                return new Vector3((Vector3)value);
            else if (attr_type == typeof(Vector4))
                return new Vector4((Vector4)value);
            else if (attr_type == typeof(Angle))
                return new Angle((Angle)value);
            else if (attr_type == typeof(Quaternion))
                return new Quaternion((Quaternion)value);
            else if (attr_type == typeof(Matrix))
                return new Matrix((Matrix)value);
            else if (attr_type == typeof(byte[]))
            {
                var inbytes = (byte[])value;
                var outbytes = new byte[inbytes.Length];
                inbytes.CopyTo(outbytes, 0);
                return outbytes;
            }

            else throw new ArgumentException("CopyValue: unhandled type.");
        }

        Element ImportElement_internal(Element foreign_element, bool deep, bool overwrite)
        {
            if (foreign_element == null) return null;
            if (foreign_element.Owner == this) return foreign_element;

            // find an existing element with the same ID and either return or replace it
            var result = AllElements[foreign_element.ID];
            if (result != null)
            {
                if (overwrite && !foreign_element.Stub)
                    AllElements.Remove(result, ElementList.RemoveMode.MakeStubs); // allow attributes to substitute this Element for the old one
                return result;
            }
            result = CreateElement(foreign_element.Name, foreign_element.ID, foreign_element.ClassName);
            
            // Copy attributes
            foreach (var attr in foreign_element)
            {
                if (attr.Value == null)
                    result[attr.Name] = null;
                else if (IsDatamodelArrayType(attr.Value.GetType()))
                {
                    var list = (System.Collections.ICollection)attr.Value;
                    var inner_type = GetArrayInnerType(list.GetType());

                    var copied_array = CodecUtilities.MakeList(inner_type, list.Count);
                    foreach (var item in list)
                        copied_array.Add(CopyValue(item, deep, overwrite));

                    result[attr.Name] = copied_array;
                }
                else
                    result[attr.Name] = CopyValue(attr.Value, deep, overwrite);
            }
            return result;
        }

        /// <summary>
        /// Creates a new stub Element.
        /// </summary>
        /// <seealso cref="Element.Stub"/>
        /// <param name="id">The ID of the stub. Must be unique within the Datamodel.</param>
        /// <returns>A new stub Element owned by this Datamodel.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when the maximum number of Elements allowed in a Datamodel has been reached.</exception>
        public Element CreateStubElement(Guid id)
        {
            return CreateElement("Stub element", id, true);
        }

        /// <summary>
        /// Creates a new Element with a random ID.
        /// </summary>
        /// <param name="name">The Element's name. Duplicates allowed.</param>
        /// <param name="class_name">The Element's class.</param>
        /// <returns>A new Element owned by this Datamodel.</returns>
        /// <exception cref="InvalidOperationException">Thrown when Datamodel.AllowRandomIDs is false.</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown when the maximum number of Elements allowed in a Datamodel has been reached.</exception>
        public Element CreateElement(string name, string class_name = "DmElement")
        {
            if (!AllowRandomIDs)
                throw new InvalidOperationException("Random IDs are not allowed in this Datamodel.");

            Guid id;
            do { id = Guid.NewGuid(); }
            while (AllElements[id] != null);
            return CreateElement(name, id, class_name);
        }

        /// <summary>
        /// Creates a new Element.
        /// </summary>
        /// <param name="name">The Element's name. Duplicates allowed.</param>
        /// <param name="id">The Element's ID. Must be unique within the Datamodel.</param>
        /// <param name="class_name">The Element's class.</param>
        /// <returns>A new Element owned by this Datamodel.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when the maximum number of Elements allowed in a Datamodel has been reached.</exception>
        public Element CreateElement(string name, Guid id, string class_name = "DmElement")
        {
            return CreateElement(name, id, false, class_name);
        }

        internal int ElementsAdded = 0; // used to optimise de-stubbing
        internal Element CreateElement(string name, Guid id, bool stub, string classname = "DmElement")
        {
            lock (AllElements.ChangeLock)
            {
                if (AllElements.Count == Int32.MaxValue) // jinkies!
                    throw new IndexOutOfRangeException("Maximum Element count reached.");

                if (AllElements[id] != null)
                    throw new ElementIdException(String.Format("Element ID {0} already in use in this Datamodel.", id.ToString()));

                if (!stub) ElementsAdded++;
            }
            return new Element(this, id, name, classname, stub);
        }
        #endregion

        #region Events
        /// <summary>
        /// Raised when the Datamodel's <see cref="Format"/>, <see cref="FormatVersion"/>, or <see cref="Root"/> changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }
        #endregion
    }

    #region Exceptions
    /// <summary>
    /// The exception that is thrown when the value of an <see cref="Attribute"/> is an unsupported <see cref="Type"/>.
    /// </summary>
    [Serializable]
    public class AttributeTypeException : ArgumentException
    {
        internal AttributeTypeException(string message)
            : base(message)
        { }

        [SecuritySafeCritical]
        protected AttributeTypeException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }

    /// <summary>
    /// The exception that is thrown when an <see cref="Element.ID"/> collision occurrs.
    /// </summary>
    [Serializable]
    public class ElementIdException : InvalidOperationException
    {
        internal ElementIdException(string message)
            : base(message)
        { }

        [SecuritySafeCritical]
        protected ElementIdException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }

    /// <summary>
    /// The exception that is thrown when a Datamodel tries to manipulate an <see cref="Element"/> with an innapropriate owner.
    /// </summary>
    [Serializable]
    public class ElementOwnershipException : InvalidOperationException
    {
        internal ElementOwnershipException(string message)
            : base(message)
        { }

        [SecuritySafeCritical]
        protected ElementOwnershipException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }

    /// <summary>
    /// The exception that is thrown when an error occurs in an <see cref="ICodec"/>.
    /// </summary>
    [Serializable]
    public class CodecException : Exception
    {
        public CodecException(string message)
            : base(message)
        { }

        public CodecException(string message, Exception innerException)
            : base(message, innerException)
        { }

        [SecuritySafeCritical]
        protected CodecException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }
    #endregion

    static class Extensions
    {
        public static Type MakeListType(this Type t)
        {
            return typeof(List<>).MakeGenericType(t);
        }
    }
}
