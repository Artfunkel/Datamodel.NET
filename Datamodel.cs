using Datamodel.Codecs;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security;
using System.Threading;
using CodecRegistration = System.Tuple<string, int>;

namespace Datamodel
{
    /// <summary>
    /// Represents a thread-safe tree of <see cref="Element"/>s.
    /// </summary>
    [DebuggerDisplay("Format = {Format,nq} {FormatVersion}, Encoding = {Encoding,nq} {EncodingVersion}")]
    [DebuggerTypeProxy(typeof(DebugView))]
    public class Datamodel : INotifyPropertyChanged, IDisposable, ISupportInitialize
    {
        internal class DebugView
        {
            public DebugView(Datamodel dm)
            {
                DM = dm;
            }
            Datamodel DM;

            public Element Root { get { return DM.Root; } }
            public ElementList AllElements { get { return DM.AllElements; } }
            public AttributeList PrefixAttributes { get { return DM.PrefixAttributes; } }
        }
        #region Attribute types
        public static Type[] AttributeTypes { get { return _AttributeTypes; } }
        static Type[] _AttributeTypes = { typeof(Element), typeof(int), typeof(float), typeof(bool), typeof(string), typeof(byte[]), 
                typeof(TimeSpan), typeof(System.Drawing.Color), typeof(Vector2), typeof(Vector3),typeof(Vector4), typeof(Angle), typeof(Quaternion), typeof(Matrix),
				typeof(byte), typeof(UInt64) };

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
            if (t == typeof(Element)) return null;
            var i_type = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>) ? t : t.GetInterface("IList`1");
            if (i_type == null) return null;

            var inner = i_type.GetGenericArguments()[0];
            return inner;
        }
        #endregion

        static Datamodel()
        {
            Datamodel.RegisterCodec(typeof(Codecs.Binary));
            Datamodel.RegisterCodec(typeof(Codecs.KeyValues2));
            TextEncoding = new System.Text.UTF8Encoding(false);
        }

        #region Codecs
        static Dictionary<CodecRegistration, Type> Codecs = new Dictionary<CodecRegistration, Type>();
        public static IEnumerable<CodecRegistration> CodecsRegistered { get { return Codecs.Keys.OrderBy(reg => String.Join(null, reg.Item1, reg.Item2)).ToArray(); } }

        /// <summary>
        /// Registers a new <see cref="ICodec"/> with an encoding name and one or more encoding versions.
        /// </summary>
        /// <remarks>Existing codecs will be replaced.</remarks>
        /// <param name="type">The ICodec implementation being registered.</param>
        public static void RegisterCodec(Type type)
        {
            if (type.GetInterface(typeof(ICodec).FullName) == null) throw new CodecException(String.Format("{0} does not implement Datamodel.Codecs.ICodec.", type.Name));
            if (type.GetConstructor(Type.EmptyTypes) == null) throw new CodecException(String.Format("{0} does not have a default constructor.", type.Name));

            var format_attrs = (CodecFormatAttribute[])type.GetCustomAttributes(typeof(CodecFormatAttribute), true);
            if (format_attrs.Length == 0)
                throw new CodecException(String.Format("{0} does not provide Datamodel.Codecs.CodecFormatAttribute.", type.Name));

            foreach (var format_attr in format_attrs)
            {
                foreach (var version in format_attr.Versions)
                {
                    var reg = new CodecRegistration(format_attr.Name, version);
                    if (Codecs.ContainsKey(reg) && Codecs[reg] != type)
                        System.Diagnostics.Trace.TraceInformation("Datamodel.NET: Replacing existing codec for {0} {1} ({2}) with {3}", format_attr.Name, version, Codecs[reg].Name, type.Name);
                    Codecs[reg] = type;
                }
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
        /// <remarks>Changing this value does not alter Datamodels which are already in memory.</remarks>
        public static System.Text.Encoding TextEncoding { get; set; }

        const string FormatBlankError = "Cannot save while Datamodel.Format is blank";

        /// <summary>
        /// Writes this Datamodel to a <see cref="Stream"/> with the given encoding and encoding version.
        /// </summary>
        /// <param name="stream">The output Stream.</param>
        /// <param name="encoding">The desired encoding.</param>
        /// <param name="encoding_version">The desired encoding version.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value of <see cref="Datamodel.Format"/> is null or whitespace.</exception>
        public void Save(Stream stream, string encoding, int encoding_version)
        {
            if (String.IsNullOrWhiteSpace(Format))
                throw new InvalidOperationException(FormatBlankError);
            GetCodec(encoding, encoding_version).Encode(this, encoding_version, stream);
        }

        /// <summary>
        /// Writes this Datamodel to a file path with the given encoding and encoding version.
        /// </summary>
        /// <param name="path">The destination file path.</param>
        /// <param name="encoding">The desired encoding.</param>
        /// <param name="encoding_version">The desired encoding version.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value of <see cref="Datamodel.Format"/> is null or whitespace.</exception>
        public void Save(string path, string encoding, int encoding_version)
        {
            if (String.IsNullOrWhiteSpace(Format))
                throw new InvalidOperationException(FormatBlankError);
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
                throw new InvalidOperationException("Could not read file header.");

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

        internal Element OnStubRequest(Guid id)
        {
            Element result = null;
            if (StubRequest != null)
            {
                result = StubRequest(id);
                if (result != null && result.ID != id)
                    throw new InvalidOperationException("Datamodel.StubRequest returned an Element with a an ID different from the one requested.");
                if (result.Owner != this)
                    result = ImportElement(result, ImportRecursionMode.Stubs, ImportOverwriteMode.Stubs);
            }

            return result ?? AllElements[id];
        }

        /// <summary>
        /// Occurs when an attempt is made to access a stub elment.
        /// </summary>
        public event StubRequestHandler StubRequest;

        #endregion

        /// <summary>
        /// A collection of <see cref="Element"/>s owned by a single <see cref="Datamodel"/>.
        /// </summary>
        [DebuggerDisplay("Count = {Count}")]
        [DebuggerTypeProxy(typeof(DebugView))]
        public class ElementList : IEnumerable<Element>, INotifyCollectionChanged, IDisposable
        {
            internal ReaderWriterLockSlim ChangeLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

            internal class DebugView
            {
                public DebugView(ElementList item)
                { Item = item; }
                ElementList Item;

                [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
                public Element[] Elements { get { return (Element[])Item.store.Values; } }
            }

            OrderedDictionary store = new OrderedDictionary();
            Datamodel Owner;

            internal ElementList(Datamodel owner)
            {
                Owner = owner;
            }

            internal void Add(Element item)
            {
                ChangeLock.EnterUpgradeableReadLock();
                try
                {
                    Element existing = (Element)store[item.ID];
                    if (existing != null && !existing.Stub)
                    {
                        throw new ElementIdException(String.Format("Element ID {0} already in use in this Datamodel.", item.ID));
                    }

                    System.Diagnostics.Debug.Assert(item.Owner != null);
                    if (item.Owner != this.Owner)
                        throw new ElementOwnershipException("Cannot add an element from a different Datamodel. Use ImportElement() to create a local copy instead.");

                    ChangeLock.EnterWriteLock();
                    try
                    {
                        store.Add(item.ID, item);

                        if (existing != null)
                            store.Remove(existing.ID);
                    }
                    finally
                    {
                        ChangeLock.ExitWriteLock();
                    }
                }
                finally
                {
                    ChangeLock.ExitUpgradeableReadLock();
                }

                if (CollectionChanged != null) CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
            }

            /// <summary>
            /// Returns the <see cref="Element"/> at the specified index.
            /// </summary>
            /// <remarks>The order of this list has no meaning to a Datamodel. This accessor is intended for <see cref="ICodec"/> implementers.</remarks>
            /// <param name="index">The index to look up.</param>
            /// <returns>The Element found at the index.</returns>
            public Element this[int index]
            {
                get
                {
                    ChangeLock.EnterReadLock();
                    try
                    {
                        return (Element)store[index];
                    }
                    finally { ChangeLock.ExitReadLock(); }
                }
            }

            /// <summary>
            /// Searches the collection for an <see cref="Element"/> with the specified <see cref="Element.ID"/>.
            /// </summary>
            /// <param name="id">The ID to search for.</param>
            /// <returns>The Element with the given ID, or null if none is found.</returns>
            public Element this[Guid id]
            {
                get
                {
                    ChangeLock.EnterReadLock();
                    try
                    {
                        return (Element)store[id];
                    }
                    finally { ChangeLock.ExitReadLock(); }
                }
            }

            /// <summary>
            /// Gets the number of <see cref="Element"/>s in this collection.
            /// </summary>
            public int Count
            {
                get
                {
                    ChangeLock.EnterReadLock();
                    try
                    {
                        return store.Count;
                    }
                    finally { ChangeLock.ExitReadLock(); }
                }
            }

            internal bool Contains(Element elem)
            {
                return store.Contains(elem);
            }

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
            /// Removes an <see cref="Element"/> from the Datamodel.
            /// </summary>
            /// <remarks>This method will access all values in the Datamodel, potentially triggering deferred loading and destubbing.</remarks>
            /// <param name="item">The Element to remove.</param>
            /// <param name="mode">The action to take if a reference to this Element is found in another Element.</param>
            /// <returns>true if item is successfully removed; otherwise, false.</returns>
            public bool Remove(Element item, RemoveMode mode)
            {
                if (item == null) throw new ArgumentNullException("item");

                ChangeLock.EnterUpgradeableReadLock();
                try
                {
                    if (store.Contains(item.ID))
                    {
                        ChangeLock.EnterWriteLock();
                        try
                        {
                            store.Remove(item.ID);
                            Element replacement = (mode == RemoveMode.MakeStubs) ? new Element(Owner, item.ID) : (Element)null;

                            foreach (Element elem in store.Values)
                            {
                                lock (elem.SyncRoot)
                                {
                                    foreach (var attr in elem.Where(a => a.Value == item).ToArray())
                                        elem[attr.Key] = replacement;
                                    foreach (var array in elem.Select(a => a.Value).OfType<IList<Element>>())
                                        for (int i = 0; i < array.Count; i++)
                                            if (array[i] == item)
                                                array[i] = replacement;
                                }
                            }
                            if (Owner.Root == item) Owner.Root = replacement;

                            item.Owner = null;
                        }
                        finally
                        {
                            ChangeLock.ExitWriteLock();
                        }

                        if (CollectionChanged != null) CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
                        return true;
                    }
                    else return false;
                }
                finally { ChangeLock.ExitUpgradeableReadLock(); }
            }

            /// <summary>
            /// Removes unreferenced Elements from the Datamodel.
            /// </summary>
            public void Trim()
            {
                ChangeLock.EnterUpgradeableReadLock();
                try
                {
                    var used = new HashSet<Element>();
                    WalkElemTree(Owner.Root, used);
                    if (used.Count == Count) return;

                    ChangeLock.EnterWriteLock();
                    try
                    {
                        foreach (var elem in this.Except(used).ToArray())
                        {
                            store.Remove(elem.ID);
                            elem.Owner = null;
                        }
                        if (CollectionChanged != null) CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, (System.Collections.IList)used));
                    }
                    finally
                    {
                        ChangeLock.ExitWriteLock();
                    }
                }
                finally
                {
                    ChangeLock.ExitUpgradeableReadLock();
                }
            }

            protected void WalkElemTree(Element elem, HashSet<Element> found)
            {
                found.Add(elem);
                foreach (var value in elem.Inner.Select(a => a.RawValue))
                {
                    var value_elem = value as Element;
                    if (value_elem != null)
                    {
                        if (found.Add(value_elem))
                            WalkElemTree(value_elem, found);
                        continue;
                    }
                    var elem_array = value as ElementArray;
                    if (elem_array != null)
                        foreach (var item in elem_array.RawList)
                        {
                            if (item != null && found.Add(item))
                                WalkElemTree(item, found);
                        }
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
                foreach (Element elem in store.Values)
                    yield return elem;
            }
            /// <summary>
            /// Raised when an <see cref="Element"/> is added, removed, or replaced.
            /// </summary>
            public event NotifyCollectionChangedEventHandler CollectionChanged;
            #endregion

            public void Dispose()
            {
                ChangeLock.Dispose();
            }
        }

        /// <summary>
        /// Creates a new Datamodel with a specified Format and FormatVersion.
        /// </summary>
        /// <param name="format">The format of the Datamodel. This is not the same as the encoding used to save or load the Datamodel.</param>
        /// <param name="format_version">The version of the format in use.</param>
        public Datamodel(string format, int format_version)
            : this()
        {
            Format = format;
            FormatVersion = format_version;
        }

        /// <summary>
        /// Creates a new Datamodel.
        /// </summary>
        public Datamodel()
        {
            AllElements = new ElementList(this);
            PrefixAttributes = new AttributeList(this);
        }

        protected bool Initialising { get; private set; }
        void ISupportInitialize.BeginInit()
        {
            if (Initialising) throw new InvalidOperationException("Datamodel is already initializing.");
            Initialising = true;
        }

        void ISupportInitialize.EndInit()
        {
            if (!Initialising) throw new InvalidOperationException("Datamodel is not initializing.");

            if (Format == null)
                throw new InvalidOperationException("A Format name must be defined.");

            Initialising = false;
        }

        /// <summary>
        /// Releases any <see cref="Stream"/> being used to deferred load the Datamodel's <see cref="Attribute"/>s.
        /// </summary>
        public void Dispose()
        {
            if (Stream != null) Stream.Dispose();
            AllElements.Dispose();
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
        /// <exception cref="ArgumentException">Thrown when an attempt is made to set a value containing the space character.</exception>
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
        /// <exception cref="ArgumentException">Thrown when an attempt is made to set a value containing the space character.</exception>
        public string Encoding
        {
            get { return _Encoding; }
            set
            {
                if (value != null && value.Contains(' '))
                    throw new ArgumentException("Encoding name cannot contain spaces.");
                _Encoding = value;
                NotifyPropertyChanged("Encoding");
            }
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
                if (value != null)
                {
                    if (value.Owner == null)
                        value = ImportElement(value, ImportRecursionMode.Recursive, ImportOverwriteMode.All);
                    else if (value.Owner != this)
                        throw new ElementOwnershipException();
                }
                _Root = value;
                NotifyPropertyChanged("Root");
            }
        }
        Element _Root;

        public AttributeList PrefixAttributes { get; protected set; }

        /// <summary>
        /// Gets all Elements owned by this Datamodel. Only Elements which are referenced by the Root element or one of its children are actually considered part of the Datamodel.
        /// </summary>
        public ElementList AllElements { get; protected set; }
        #endregion

        #region Element handling
        /// <summary>
        /// Copies another Datamodel's <see cref="Element"/> into this one.
        /// </summary>
        /// <remarks>The return value will be owned by this Datamodel. It can be:
        /// 1. foreign_element. This is the case when foreign_element has no owner and no Element with the same ID exists in this Datamodel.
        /// 2. An existing Element owned by this Datamodel with the same ID as foreign_element.
        /// 3. A copy of foreign_element. This is the case when foreign_element already had an owner and a corresponding Element was not found in this Datamodel.</remarks>
        /// <param name="foreign_element">The Element to import. Must be owned by a different Datamodel.</param>
        /// <param name="import_mode">How to respond when foreign_element references other foreign Elements.</param>
        /// <param name="overwrite_mode">How to respond when the ID of a foreign Element is already in use in this Datamodel.</param>
        /// <returns>foreign_element, a local Element, or a new copy of foreign_element. See Remarks for more details.</returns>
        /// <exception cref="ArgumentNullException">Thrown if foreign_element is null.</exception>
        /// <exception cref="ElementOwnershipException">Thrown if foreign_element is already owned by this Datamodel.</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown when the maximum number of Elements allowed in a Datamodel has been reached.</exception>
        /// <seealso cref="Element.Stub"/>
        /// <seealso cref="Element.ID"/>
        public Element ImportElement(Element foreign_element, ImportRecursionMode import_mode, ImportOverwriteMode overwrite_mode)
        {
            if (foreign_element == null) throw new ArgumentNullException("element");
            if (foreign_element.Owner == this) throw new ElementOwnershipException("Element is already a part of this Datamodel.");

            return ImportElement_internal(foreign_element, new ImportJob(import_mode, overwrite_mode));
        }

        public enum ImportRecursionMode
        {
            /// <summary>
            /// Import the given Element only. Any Element reference will become null.
            /// </summary>
            Nulls,
            /// <summary>
            /// Import the given Element only. Any Element references will be represented with stubs.
            /// </summary>
            Stubs,
            /// <summary>
            /// Recursively import all referenced Elements.
            /// </summary>
            Recursive,
        }

        public enum ImportOverwriteMode
        {
            /// <summary>
            /// If a local Element has the same ID as a foreign Element, ignore the foreign Element.
            /// </summary>
            None,
            /// <summary>
            /// If a local stub Element has the same ID as a non-stub foreign Element, replace it.
            /// </summary>
            Stubs,
            /// <summary>
            /// If a local Element has the same ID as a non-stub foreign Element, replace it.
            /// </summary>
            All,
        }

        struct ImportJob
        {
            public ImportRecursionMode ImportMode { get; private set; }
            public ImportOverwriteMode OverwriteMode { get; private set; }
            public Dictionary<Element, Element> ImportMap { get; private set; }
            public int Depth { get; set; }

            public ImportJob(ImportRecursionMode import_mode, ImportOverwriteMode overwrite_mode)
                : this()
            {
                ImportMode = import_mode;
                OverwriteMode = overwrite_mode;
                ImportMap = new Dictionary<Element, Element>();
            }
        }

        object CopyValue(object value, ImportJob job)
        {
            if (value == null) return null;
            var attr_type = value.GetType();

            // do nothing for a value type or string...
            if (attr_type.IsValueType || attr_type == typeof(string))
                return value;

            // ...copy a reference type
            else if (attr_type == typeof(Element))
            {
                var foreign_element = (Element)value;
                var local_element = AllElements[foreign_element.ID];
                Element best_element = null;

                if (local_element != null && !local_element.Stub)
                    best_element = local_element;
                else if (!foreign_element.Stub && job.ImportMode == ImportRecursionMode.Recursive)
                {
                    job.Depth++;
                    best_element = ImportElement_internal(foreign_element, job);
                    job.Depth--;
                }
                else
                    best_element = local_element ?? (job.ImportMode == ImportRecursionMode.Stubs ? new Element(this, foreign_element.ID) : (Element)null);

                return best_element;
            }
            else if (attr_type == typeof(byte[]))
            {
                var inbytes = (byte[])value;
                var outbytes = new byte[inbytes.Length];
                inbytes.CopyTo(outbytes, 0);
                return outbytes;
            }

            else throw new ArgumentException("CopyValue: unhandled type.");
        }

        Element ImportElement_internal(Element foreign_element, ImportJob job)
        {
            if (foreign_element == null) return null;
            if (foreign_element.Owner == this) return foreign_element;

            Element local_element;

            // don't import the same Element twice
            if (job.ImportMap.TryGetValue(foreign_element, out local_element))
                return local_element;

            lock (foreign_element.SyncRoot)
            {
                // Claim an unowned Element
                if (foreign_element.Owner == null && AllElements[foreign_element.ID] == null)
                {
                    foreign_element.Owner = this;
                    foreach (var attr in foreign_element)
                    {
                        var elem = attr.Value as Element;
                        if (elem != null)
                            foreign_element[attr.Key] = ImportElement_internal(elem, job);

                        var elem_array = attr.Value as ElementArray;
                        if (elem_array != null)
                        {
                            elem_array.Owner = foreign_element;
                            for (int i = 0; i < elem_array.Count; i++)
                            {
                                var item = elem_array[i];
                                if (item != null && item.Owner != null)
                                    elem_array[i] = ImportElement_internal(item, job);
                            }
                        }
                    }
                    return foreign_element;
                }

                // find a local element with the same ID and either return or stub it
                local_element = AllElements[foreign_element.ID];
                if (local_element != null)
                {
                    if (!foreign_element.Stub && (job.OverwriteMode == ImportOverwriteMode.All || (job.OverwriteMode == ImportOverwriteMode.Stubs && local_element.Stub)))
                    {
                        local_element.Name = foreign_element.Name;
                        local_element.ClassName = foreign_element.ClassName;
                        local_element.Stub = false;
                    }
                    else
                        return local_element;
                }
                else
                {
                    // Create a new local Element
                    if (foreign_element.Stub || (job.ImportMode == ImportRecursionMode.Stubs && job.Depth > 0))
                        local_element = new Element(this, foreign_element.ID);
                    else if (job.ImportMode == ImportRecursionMode.Recursive || job.Depth == 0)
                        local_element = new Element(this, foreign_element.Name, foreign_element.ID, foreign_element.ClassName);
                    else
                        local_element = null;
                }
                job.ImportMap.Add(foreign_element, local_element);

                // Copy attributes
                if (local_element != null && !local_element.Stub)
                {
                    local_element.Clear();
                    foreach (var attr in foreign_element)
                    {
                        if (attr.Value == null)
                            local_element[attr.Key] = null;
                        else if (IsDatamodelArrayType(attr.Value.GetType()))
                        {
                            var list = (System.Collections.ICollection)attr.Value;
                            var inner_type = GetArrayInnerType(list.GetType());

                            var copied_array = CodecUtilities.MakeList(inner_type, list.Count);
                            foreach (var item in list)
                                copied_array.Add(CopyValue(item, job));

                            local_element[attr.Key] = copied_array;
                        }
                        else
                            local_element[attr.Key] = CopyValue(attr.Value, job);
                    }
                }
                return local_element;
            }
        }

        #region Deprecated methods

        /// <summary>
        /// Creates a new stub Element.
        /// </summary>
        /// <seealso cref="Element.Stub"/>
        /// <param name="id">The ID of the stub. Must be unique within the Datamodel.</param>
        /// <returns>A new stub Element owned by this Datamodel.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when the maximum number of Elements allowed in a Datamodel has been reached.</exception>
        [Obsolete("Elements should now be constructed directly.")]
        public Element CreateStubElement(Guid id)
        {
            return CreateElement(null, id, true);
        }

        /// <summary>
        /// Creates a new Element with a random ID.
        /// </summary>
        /// <param name="name">The Element's name. Duplicates allowed.</param>
        /// <param name="class_name">The Element's class.</param>
        /// <returns>A new Element owned by this Datamodel.</returns>
        /// <exception cref="InvalidOperationException">Thrown when Datamodel.AllowRandomIDs is false.</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown when the maximum number of Elements allowed in a Datamodel has been reached.</exception>
        [Obsolete("Elements should now be constructed directly.")]
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
        [Obsolete("Elements should now be constructed directly.")]
        public Element CreateElement(string name, Guid id, string class_name = "DmElement")
        {
            return CreateElement(name, id, false, class_name);
        }

        [Obsolete("Elements should now be constructed directly.")]
        internal Element CreateElement(string name, Guid id, bool stub, string classname = "DmElement")
        {
            return stub ? new Element(this, id) : new Element(this, name, id, classname);
        }

        #endregion

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

    /// <summary>
    /// Represents the method that will handle the <see cref="Datamodel.StubRequest"/> event.
    /// </summary>
    /// <param name="id">The Element ID to search for.</param>
    /// <returns>An Element with the requested ID.</returns>
    public delegate Element StubRequestHandler(Guid id);

    #region Exceptions
    /// <summary>
    /// The exception that is thrown when the value of an <see cref="Attribute"/> is an unsupported <see cref="Type"/>.
    /// </summary>
    [Serializable]
    public class AttributeTypeException : ArgumentException
    {
        public AttributeTypeException(string message)
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

        internal ElementOwnershipException()
            : base("Cannot add an Element from a different Datamodel. Use ImportElement() first.")
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

    /// <summary>
    /// The exception that is thrown when an error occurs while destubbing an attribute value.
    /// </summary>
    [Serializable]
    public class DestubException : Exception
    {
        internal DestubException(Attribute attr, Exception innerException)
            : base("An exception occured while destubbing the value of an attribute.", innerException)
        {
            Data.Add("Element", ((Element)attr.Owner).ID);
            Data.Add("Attribute", attr.Name);
        }

        internal DestubException(ElementArray array, int index, Exception innerException)
            : base("An exception occured while destubbing an array item.", innerException)
        {
            Data.Add("Element", ((Element)array.Owner).ID);
            Data.Add("Index", index);
        }

        [SecuritySafeCritical]
        protected DestubException(SerializationInfo info, StreamingContext context)
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
