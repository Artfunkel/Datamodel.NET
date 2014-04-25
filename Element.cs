using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;

using AttrKVP = System.Collections.Generic.KeyValuePair<string, object>;

namespace Datamodel
{
    /// <summary>
    /// A thread-safe collection of <see cref="Attribute"/>s. Declares a name, class name, and unique (to the owning <see cref="Datamodel"/>) ID.
    /// </summary>
    /// <remarks>Recursion is allowed, i.e. an <see cref="Attribute"/> can refer to an <see cref="Element"/> which is higher up the tree.</remarks>
    /// <seealso cref="Attribute"/>
    [DefaultProperty("Name")]
    [TypeConverter(typeof(TypeConverters.ElementConverter))]
    public class Element : IDictionary<string, object>, IDictionary, INotifyPropertyChanged, INotifyCollectionChanged, ISupportInitialize
    {
        #region Constructors and Init

        /// <summary>
        /// Creates a new Element with a specified name, optionally specifying an ID and class name.
        /// </summary>
        /// <param name="owner">The owner of this Element. Cannot be null.</param>
        /// <param name="id">A GUID that must be unique within the owning Datamodel. Can be null, in which case a random GUID is generated.</param>
        /// <param name="name">An arbitrary string. Does not have to be unique, and can be null.</param>
        /// <param name="class_name">An arbitrary string which loosely defines the type of Element this is. Cannot be null.</param>
        /// <exception cref="IndexOutOfRangeException">Thrown when the owner already contains the maximum number of Elements allowed in a Datamodel.</exception>
        public Element(Datamodel owner, string name, Guid? id = null, string class_name = "DmElement")
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (class_name == null) throw new ArgumentNullException("class_name");

            Name = name;
            ClassName = class_name;

            if (id.HasValue)
                _ID = id.Value;
            else
            {
                if (!owner.AllowRandomIDs) throw new InvalidOperationException("Random IDs are not allowed in this Datamodel.");
                _ID = Guid.NewGuid();
            }
            Owner = owner;
        }

        /// <summary>
        /// Creates a new stub Element to represent an Element in another Datamodel.
        /// </summary>
        /// <seealso cref="Element.Stub"/>
        /// <param name="owner">The owner of this Element. Cannot be null.</param>
        /// <param name="id">The ID of the remote Element that this stub represents.</param>
        public Element(Datamodel owner, Guid id)
        {
            if (owner == null) throw new ArgumentNullException("owner");

            _ID = id;
            Stub = true;
            Name = "Stub element";
            Owner = owner;
        }

        /// <summary>
        /// Creates a new Element with a random GUID.
        /// </summary>
        public Element()
        {
            _ID = Guid.NewGuid();
        }

        bool Initialising = false;
        void ISupportInitialize.BeginInit()
        {
            Initialising = true;
        }

        void ISupportInitialize.EndInit()
        {
            if (ID == null)
                ID = Guid.NewGuid();

            Initialising = false;
        }

        #endregion

        private List<Attribute> Attributes = new List<Attribute>();
        private object Attribute_ChangeLock = new object();

        #region Properties

        /// <summary>
        /// Gets the ID of this Element. This must be unique within the Element's <see cref="Datamodel"/>.
        /// </summary>
        public Guid ID
        {
            get { return _ID; }
            set
            {
                if (!Initialising) throw new InvalidOperationException("ID can only be changed during initialisation.");
                _ID = value;
            }
        }
        Guid _ID;

        /// <summary>
        /// Gets or sets the name of this Element.
        /// </summary>
        public string Name
        {
            get { return _Name; }
            set { _Name = value; OnPropertyChanged("Name"); }
        }
        string _Name;

        /// <summary>
        /// Gets or sets the class of this Element. This is a string which loosely defines what <see cref="Attribute"/>s the Element contains.
        /// </summary>
        [DefaultValue("DmeElement")]
        public string ClassName
        {
            get { return _ClassName; }
            set { _ClassName = value; OnPropertyChanged("ClassName"); }
        }
        string _ClassName = "DmeElement";

        /// <summary>
        /// Gets or sets whether this Element is a stub.
        /// </summary>
        /// <remarks>A Stub element does (or did) exist, but is not defined in this Element's <see cref="Datamodel"/>. Only its <see cref="ID"/> is known.</remarks>
        public bool Stub
        {
            get { return _Stub; }
            set
            {
                if (value && Count > 0) throw new InvalidOperationException("An Element containing Attributes cannot be a Stub.");
                _Stub = value;
            }
        }
        bool _Stub;

        /// <summary>
        /// Gets the <see cref="Datamodel"/> that this Element is owned by.
        /// </summary>
        public Datamodel Owner
        {
            get { return _Owner; }
            internal set
            {
                if (_Owner != null) throw new InvalidOperationException("Element already has an owner.");
                _Owner = value;
                if (value != null)
                {
                    value.AllElements.ChangeLock.EnterWriteLock();
                    try
                    {
                        value.AllElements.Add(this);
                        if (value.AllElements.Count == 1) value.Root = this;
                    }
                    finally { value.AllElements.ChangeLock.ExitWriteLock(); }
                }
            }
        }
        Datamodel _Owner;

        #endregion

        /// <summary>
        /// Returns the value of the <see cref="Attribute"/> with the specified type and name. An exception is thrown there is no Attribute of the given name and type.
        /// </summary>
        /// <seealso cref="GetArray&lt;T&gt;"/>
        /// <typeparam name="T">The expected Type of the Attribute.</typeparam>
        /// <param name="name">The Attribute name to search for.</param>
        /// <returns>The value of the Attribute with the given name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the value of name is null.</exception>
        /// <exception cref="AttributeTypeException">Thrown when the value of the requested Attribute is not compatible with T.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when an attempt is made to get a name that is not present on this Element.</exception>
        public T Get<T>(string name)
        {
            object value = this[name];

            if (!(value is T) && !(typeof(T) == typeof(Element) && value == null))
                throw new AttributeTypeException(String.Format("Attribute \"{0}\" ({1}) does not implement {2}.", name, value.GetType().Name, typeof(T).Name));

            return (T)value;
        }

        /// <summary>
        /// Returns the value of the <see cref="Attribute"/> with the specified type and name, if it is an array. An exception is thrown there is no array Attribute of the given name and type.
        /// </summary>
        /// <remarks>This is a convenience function that calls <see cref="Get&lt;T&gt;"/>.</remarks>
        /// <typeparam name="T">The expected <see cref="Type"/> of the array's items.</typeparam>
        /// <param name="name">The name to search for.</param>
        /// <returns>The value of the Attribute with the given name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the value of name is null.</exception>
        /// <exception cref="AttributeTypeException">Thrown when the value of the requested Attribute is not compatible with IList&lt;T&gt;.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when an attempt is made to get a name that is not present on this Element.</exception>
        public IList<T> GetArray<T>(string name)
        {
            try
            {
                return Get<IList<T>>(name);
            }
            catch (AttributeTypeException)
            {
                throw new AttributeTypeException(String.Format("Attribute \"{0}\" ({1}) is not an array.", name, this[name].GetType().Name));
            }

        }

        /// <summary>
        /// Gets or sets the value of the <see cref="Attribute"/> with the given name.
        /// </summary>
        /// <param name="name">The name to search for. Cannot be null.</param>
        /// <returns>The value associated with the given name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the value of name is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when an attempt is made to get a name that is not present on this Element.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an attempt is made to get or set an attribute on a <see cref="Stub"/> Element.</exception>
        /// <exception cref="ElementOwnershipException">Thrown when an attempt is made to set the value of the attribute to an Element from a different <see cref="Datamodel"/>.</exception>
        /// <exception cref="AttributeTypeException">Thrown when an attempt is made to set a value that is not of a valid Datamodel attribute type.</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown when the maximum number of Attributes allowed in an Element has been reached.</exception>
        public object this[string name]
        {
            get
            {
                if (name == null) throw new ArgumentNullException("name");
                if (Stub) throw new InvalidOperationException("Cannot get attributes from a stub element.");
                var attr = Attributes.Find(a => a.Name == name);
                if (attr.Name == null) throw new KeyNotFoundException(String.Format("{0} does not have an attribute called \"{1}\"", this, name));
                return attr.Value;
            }
            set
            {
                if (name == null) throw new ArgumentNullException("name");
                if (Stub) throw new InvalidOperationException("Cannot set attributes on a stub element.");

                if (value != null && !Datamodel.IsDatamodelType(value.GetType()))
                    throw new AttributeTypeException(String.Format("{0} is not a valid Datamodel attribute type. (If this is an array, it must implement IList<T>).", value.GetType().FullName));


                Attribute old_attr, new_attr;
                int old_index;
                lock (Attribute_ChangeLock)
                {
                    old_attr = Attributes.Find(a => a.Name == name);
                    new_attr = new Attribute(name, this, value);

                    old_index = Attributes.IndexOf(old_attr);
                    Insert(old_index == -1 ? Count : old_index, new Attribute(name, this, value));

                    if (old_attr.Name != null)
                        Attributes.Remove(old_attr);
                }

                NotifyCollectionChangedEventArgs change_args;
                if (old_attr.Name != null)
                    change_args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, new_attr.ToKeyValuePair(), old_attr.ToKeyValuePair(), old_index);
                else
                    change_args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new_attr.ToKeyValuePair(), Count);

                OnCollectionChanged(change_args);
            }
        }

        /// <summary>
        /// Removes an <see cref="Attribute"/> by name.
        /// </summary>
        /// <param name="name">The name to search for</param>
        /// <returns>true if item is successfully removed; otherwise, false. This method also returns false if item was not found.</returns>
        public bool Remove(string name)
        {
            lock (Attribute_ChangeLock)
            {
                var attr = Attributes.FirstOrDefault(a => a.Name == name);
                if (attr.Name == null) return false;
                var index = Attributes.IndexOf(attr);
                if (Attributes.Remove(attr))
                {
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, attr.ToKeyValuePair(), index));
                    return true;
                }
                else return false;
            }
        }

        /// <summary>
        /// Removes all Attributes from the Element.
        /// </summary>
        public void Clear()
        {
            lock (Attribute_ChangeLock)
                Attributes.Clear();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        /// <summary>
        /// Gets the number of <see cref="Attribute"/>s in the Element.
        /// </summary>
        public int Count
        {
            get
            {
                lock (Attribute_ChangeLock)
                    return Attributes.Count;
            }
        }

        /// <summary>
        /// Gets or sets the attribute at the given index.
        /// </summary>
        public AttrKVP this[int index]
        {
            get
            {
                var attr = Attributes[index];
                return attr.ToKeyValuePair();
            }
            set
            {
                RemoveAt(index);
                Insert(index, new Attribute(value.Key, this, value.Value));
            }
        }

        /// <summary>
        /// Inserts an Attribute at the given index.
        /// </summary>
        private void Insert(int index, Attribute item)
        {
            lock (Attribute_ChangeLock)
            {
                if (Attributes.Count == Int32.MaxValue)
                    throw new IndexOutOfRangeException(String.Format("Maximum Attribute count reached for Element {0}.", ID));

                Attributes.Insert(index, item);
            }
            item.Owner = this;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item.ToKeyValuePair(), index));
        }

        /// <summary>
        /// Removes the attribute at the given index.
        /// </summary>
        public void RemoveAt(int index)
        {
            Attribute attr;
            lock (Attribute_ChangeLock)
            {
                attr = Attributes[index];
                attr.Owner = null;
                Attributes.RemoveAt(index);
            }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, attr, index));
        }

        [Obsolete("Use the ContainsKey method.")]
        public bool Contains(string name)
        {
            return ContainsKey(name);
        }

        public int IndexOf(string key)
        {
            lock (Attribute_ChangeLock)
            {
                int i = 0;
                foreach (var attr in Attributes)
                    if (attr.Name == key) return i;
            }
            return -1;
        }

        #region Interfaces

        /// <summary>
        /// Raised when <see cref="Element.Name"/>, <see cref="Element.ClassName"/>, or <see cref="Element.ID"/> has changed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }

        /// <summary>
        /// Raised when an <see cref="Attribute"/> is added, removed, or replaced.
        /// </summary>
        public event NotifyCollectionChangedEventHandler CollectionChanged;
        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.Assert(!(e.NewItems != null && e.NewItems.OfType<Attribute>().Any()) && !(e.OldItems != null && e.OldItems.OfType<Attribute>().Any()));

            OnPropertyChanged("Item[]"); // this is the magic value of System.Windows.Data.Binding.IndexerName that tells the binding engine an indexer has changed
            
            if (CollectionChanged != null)
                CollectionChanged(this, e);
        }

        public bool IsReadOnly { get { return false; } }
        public bool IsSynchronized { get { return true; } }

        #region Explicit interfaces


        /// <summary>
        /// Gets an object which can be used to synchronise access to this Element's Attributes.
        /// </summary>
        public object SyncRoot { get { return Attribute_ChangeLock; } }


        #endregion

        #endregion

        public override string ToString()
        {
            return String.Format("{0}[{1}]", Name, ClassName);
        }

        #region IEqualityComparer
        /// <summary>
        /// Compares two <see cref="Element"/>s for equivalence by using their Names.
        /// </summary>
        public class NameComparer : IEqualityComparer, IEqualityComparer<Element>
        {
            /// <summary>
            /// Gets a default Element Name equality comparer.
            /// </summary>
            public static NameComparer Default
            {
                get
                {
                    if (_Default == null)
                        _Default = new NameComparer();
                    return _Default;
                }
            }
            static NameComparer _Default;

            public bool Equals(Element x, Element y)
            {
                return x.Name == y.Name;
            }

            public int GetHashCode(Element obj)
            {
                return obj.Name.GetHashCode();
            }

            bool IEqualityComparer.Equals(object x, object y)
            {
                return Equals((Element)x, (Element)y);
            }

            int IEqualityComparer.GetHashCode(object obj)
            {
                return GetHashCode((Element)obj);
            }
        }
        /// <summary>
        /// Compares two <see cref="Element"/>s for equivalence by using their ClassNames.
        /// </summary>
        public class ClassNameComparer : IEqualityComparer, IEqualityComparer<Element>
        {
            /// <summary>
            /// Gets a default Element ClassName equality comparer.
            /// </summary>
            public static ClassNameComparer Default
            {
                get
                {
                    if (_Default == null)
                        _Default = new ClassNameComparer();
                    return _Default;
                }
            }
            static ClassNameComparer _Default;

            public bool Equals(Element x, Element y)
            {
                return x.ClassName == y.ClassName;
            }

            public int GetHashCode(Element obj)
            {
                return obj.ClassName.GetHashCode();
            }

            bool IEqualityComparer.Equals(object x, object y)
            {
                return Equals((Element)x, (Element)y);
            }

            int IEqualityComparer.GetHashCode(object obj)
            {
                return GetHashCode((Element)obj);
            }
        }
        /// <summary>
        /// Compares two <see cref="Element"/>s for equivalence by using their IDs.
        /// </summary>
        public class IDComparer : IEqualityComparer, IEqualityComparer<Element>
        {
            /// <summary>
            /// Gets a default Element ID equality comparer.
            /// </summary>
            public static IDComparer Default
            {
                get
                {
                    if (_Default == null)
                        _Default = new IDComparer();
                    return _Default;
                }
            }
            static IDComparer _Default;

            public bool Equals(Element x, Element y)
            {
                return x.ID == y.ID;
            }

            public int GetHashCode(Element obj)
            {
                return obj.ID.GetHashCode();
            }

            bool IEqualityComparer.Equals(object x, object y)
            {
                return Equals((Element)x, (Element)y);
            }

            int IEqualityComparer.GetHashCode(object obj)
            {
                return GetHashCode((Element)obj);
            }
        }


        #endregion

        /// <summary>
        /// Adds a new attribute to this Element.
        /// </summary>
        /// <param name="key">The name of the attribute. Must be unique to this Element.</param>
        /// <param name="value">The value of the Attribute. Must be of a valid Datamodel type.</param>
        public void Add(string key, object value)
        {
            this[key] = value;
        }

        /// <summary>
        /// Adds a new deferred attribute to this Element.
        /// </summary>
        /// <param name="key">The name of the attribute. Must be unique to this Element.</param>
        /// <param name="offset">The location of the attribute's value in the Datamodel's source stream.</param>
        internal void Add(string key, long offset)
        {
            lock (Attribute_ChangeLock)
                Attributes.Add(new Attribute(key, this, offset));
        }

        public bool ContainsKey(string key)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (Stub) throw new InvalidOperationException("Cannot access attributes on a stub element.");
            lock (Attribute_ChangeLock)
                return Attributes.Any(a => a.Name == key);
        }

        public ICollection<string> Keys
        {
            get { lock (Attribute_ChangeLock) return Attributes.Select(a => a.Name).ToArray(); }
        }

        public bool TryGetValue(string key, out object value)
        {
            Attribute result;
            lock (Attribute_ChangeLock)
                result = Attributes.FirstOrDefault(a => a.Name == key);

            if (result.Name != null)
            {
                value = result.Value;
                return true;
            }
            else
            {
                value = null;
                return false;
            }
        }

        public ICollection<object> Values
        {
            get { lock (Attribute_ChangeLock) return Attributes.Select(a => a.Value).ToArray(); }
        }

        void ICollection<AttrKVP>.Add(AttrKVP item)
        {
            this[item.Key] = item.Value;
        }

        bool ICollection<AttrKVP>.Contains(AttrKVP item)
        {
            lock (Attribute_ChangeLock)
                return Attributes.Any(a => a.Name == item.Key && a.Value == item.Value);
        }

        void ICollection<AttrKVP>.CopyTo(AttrKVP[] array, int arrayIndex)
        {
            lock (Attribute_ChangeLock)
                foreach (var attr in Attributes)
                {
                    array[arrayIndex] = attr.ToKeyValuePair();
                    arrayIndex++;
                }
        }

        public IEnumerator<AttrKVP> GetEnumerator()
        {
            foreach (var attr in Attributes.ToArray())
                yield return attr.ToKeyValuePair();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        void IDictionary.Add(object key, object value)
        {
            Add((string)key, value);
        }

        public bool Contains(object key)
        {
            return ContainsKey((string)key);
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public bool IsFixedSize { get { return false; } }

        ICollection IDictionary.Keys
        {
            get { lock (Attribute_ChangeLock) return Attributes.Select(a => a.Name).ToArray(); }
        }

        void IDictionary.Remove(object key)
        {
            Remove((string)key);
        }

        ICollection IDictionary.Values
        {
            get { lock (Attribute_ChangeLock) return Attributes.Select(a => a.Value).ToArray(); }
        }

        object IDictionary.this[object key]
        {
            get
            {
                return this[(string)key];
            }
            set
            {
                this[(string)key] = value;
            }
        }

        bool ICollection<AttrKVP>.Remove(AttrKVP item)
        {
            lock (Attribute_ChangeLock)
            {
                var attr = Attributes.Find(a => a.Name == item.Key);
                if (attr.Name == null || attr.Value != item.Value) return false;
                Remove(attr.Name);
                return true;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            lock (Attribute_ChangeLock)
                foreach (var attr in Attributes)
                {
                    array.SetValue(attr.ToKeyValuePair(), index);
                    index++;
                }
        }
    }

    namespace TypeConverters
    {
        public class ElementConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                if (sourceType == typeof(string) || sourceType == typeof(Guid)) return true;
                return base.CanConvertFrom(context, sourceType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
            {
                Guid guid_value;

                var str_value = value as string;
                if (str_value != null)
                    guid_value = Guid.Parse(str_value);
                else if (value is Guid)
                    guid_value = (Guid)value;
                else
                    return base.ConvertFrom(context, culture, value);

                var result = new Element();

                var ir = (ISupportInitialize)result;
                ir.BeginInit();
                result.Stub = true;
                result.ID = guid_value;
                ir.EndInit();

                return result;
            }
        }
    }
}
