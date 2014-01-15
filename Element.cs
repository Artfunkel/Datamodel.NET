using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using IEnumerator = System.Collections.IEnumerator;
using IEnumerable = System.Collections.IEnumerable;

namespace Datamodel
{
    /// <summary>
    /// A thread-safe collection of <see cref="Attribute"/>s with an associated name, class name, and unique ID.
    /// </summary>
    /// <seealso cref="Attribute"/>
    public class Element : IEnumerable<Attribute>, INotifyPropertyChanged, INotifyCollectionChanged
    {
        internal Element(Datamodel datamodel, Guid id, string name, string class_name, bool stub)
        {
            this.id = id;
            Name = name;
            ClassName = class_name;
            Stub = stub;
            if (!stub)
            {
                Owner = datamodel;
                Owner.AllElements.Add(this);
                if (Owner.AllElements.Count == 1) Owner.Root = this;
            }
        }

        internal List<Attribute> Attributes = new List<Attribute>();
        private object Attribute_ChangeLock = new object();

        #region Properties
        /// <summary>
        /// Gets the ID of this Element. This must be unique within the Element's <see cref="Datamodel"/>.
        /// </summary>
        public Guid ID { get { return id; } }
        readonly Guid id;

        /// <summary>
        /// Gets or sets the name of this Element.
        /// </summary>
        public string Name
        {
            get { return name; }
            set { name = value; NotifyPropertyChanged("Name"); }
        }
        string name;

        /// <summary>
        /// Gets or sets the class of this Element. This is a string which loosely defines what <see cref="Attribute"/>s the Element contains.
        /// </summary>
        public string ClassName
        {
            get { return className; }
            set { className = value; NotifyPropertyChanged("ClassName"); }
        }
        string className;

        /// <summary>
        /// A Stub element does (or did) exist, but is not defined in this Element's <see cref="Datamodel"/>. Only its <see cref="ID"/> is known.
        /// </summary>
        public readonly bool Stub;

        /// <summary>
        /// The <see cref="Datamodel"/> that this Element is part of.
        /// </summary>
        public readonly Datamodel Owner;
        #endregion

        /// <summary>
        /// Returns the value of the <see cref="Attribute"/> with the specified type and name. An exception is thrown there is no Attribute of the given name and type.
        /// </summary>
        /// <seealso cref="GetArray&lt;T&gt;"/>
        /// <typeparam name="T">The expected <see cref="ClassName"/> of the Attribute.</typeparam>
        /// <param name="name">The Attribute name to search for.</param>
        /// <returns>The value of the Attribute with the given name.</returns>
        public T Get<T>(string name)
        {
            if (!Datamodel.IsDatamodelType(typeof(T))) throw new AttributeTypeException(typeof(T).Name + " is not a valid Datamodel attribute type.");

            object value = this[name];

            if (!(value is T))
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
        /// Gets or sets the value of the <see cref="Attribute"/> with the given name. Raises an exception if no such Attribute exists.
        /// </summary>
        /// <param name="name">The name to search for. Cannot be null.</param>
        /// <returns>The value associated with the given name.</returns>
        public object this[string name]
        {
            get
            {
                if (name == null) throw new ArgumentNullException("name");
                if (Stub) throw new InvalidOperationException("Cannot access attributes on a stub element.");

                Attribute attr = Attributes.Find(a => a.Name == name);
                if (attr == null) throw new IndexOutOfRangeException(String.Format("Attribute \"{0}\" not found on <{1}>.", name, this));

                return attr.Value;
            }
            set
            {
                if (name == null) throw new ArgumentNullException("name");
                if (Stub) throw new InvalidOperationException("Cannot set attributes on a stub element.");

                if (value != null && !Datamodel.IsDatamodelType(value.GetType()))
                    throw new AttributeTypeException(String.Format("{0} is not a valid Datamodel attribute type. Array values must implement IList<T>.", value.GetType().FullName));

                if (value is Element)
                {
                    var elem = value as Element;
                    if (elem.Owner != null && elem.Owner != Owner)
                        throw new InvalidOperationException("Cannot add an Element from a different Datamodel. Use Datamodel.ImportElement() or Datamodel.CreateStub() instead.");
                }

                Attribute old_attr, new_attr;
                lock (Attribute_ChangeLock)
                {
                    old_attr = Attributes.Find(a => a.Name == name);
                    new_attr = new Attribute(this, name, value, 0); // the constructor adds to the store itself
                }

                NotifyCollectionChangedEventArgs change_args;
                if (old_attr != null)
                {
                    Attributes.Remove(old_attr);
                    change_args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, new_attr, old_attr);
                }
                else
                    change_args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new_attr);

                if (CollectionChanged != null)
                    CollectionChanged(this, change_args);
            }
        }

        public bool Remove(Attribute item)
        {
            lock (Attribute_ChangeLock)
            {
                var index = Attributes.IndexOf(item);
                if (Attributes.Remove(item))
                {
                    if (CollectionChanged != null)
                        CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
                    return true;
                }
                else return false;
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
                return Remove(Attributes.FirstOrDefault(a => a.Name == name));
        }

        bool Contains(string name)
        {
            return Attributes.Any(a => a.Name == name);
        }
        /// <summary>
        /// Removes all Attributes from the Element.
        /// </summary>
        public void Clear()
        {
            Attributes.Clear();
        }
        /// <summary>
        /// Gets the number of <see cref="Attribute"/>s in the Element.
        /// </summary>
        public int Count { get { return Attributes.Count; } }

        #region Interfaces
        /// <summary>
        /// Raised when an <see cref="Attribute"/> is added, removed, or replaced.
        /// </summary>
        public event NotifyCollectionChangedEventHandler CollectionChanged;
        /// <summary>
        /// Raised when <see cref="Element.Name"/>, <see cref="Element.ClassName"/>, or <see cref="Element.ID"/> has changed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }

        internal void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (CollectionChanged != null)
                CollectionChanged(this, e);
        }

        /// <summary>
        /// Returns an enumerator which iterates through this Element's <see cref="AttributeCollection"/>.
        /// </summary>
        public IEnumerator<Attribute> GetEnumerator()
        {
            return Attributes.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return Attributes.GetEnumerator() as System.Collections.IEnumerator;
        }
        #endregion

        public override string ToString()
        {
            return String.Format("{0} [{1}]", Name, ClassName);
        }

        #region IEqualityComparer
        /// <summary>
        /// Compares two <see cref="Element"/>s for equivalence by using their Names.
        /// </summary>
        public class NameComparer : IEqualityComparer<Element>
        {
            public bool Equals(Element x, Element y)
            {
                return x.Name == y.Name;
            }

            public int GetHashCode(Element obj)
            {
                return obj.Name.GetHashCode();
            }
        }
        /// <summary>
        /// Compares two <see cref="Element"/>s for equivalence by using their ClassNames.
        /// </summary>
        public class ClassNameComparer : IEqualityComparer<Element>
        {
            public bool Equals(Element x, Element y)
            {
                return x.ClassName == y.ClassName;
            }

            public int GetHashCode(Element obj)
            {
                return obj.ClassName.GetHashCode();
            }
        }
        /// <summary>
        /// Compares two <see cref="Element"/>s for equivalence by using their IDs.
        /// </summary>
        public class IDComparer : IEqualityComparer<Element>
        {
            public bool Equals(Element x, Element y)
            {
                return x.ID == y.ID;
            }

            public int GetHashCode(Element obj)
            {
                return obj.ID.GetHashCode();
            }
        }
        #endregion
    }
}
