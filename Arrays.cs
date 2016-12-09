using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Drawing;
using System.Threading;
using System.ComponentModel;

namespace Datamodel
{
    [DebuggerTypeProxy(typeof(Array<>.DebugView))]
    [DebuggerDisplay("Count = {Inner.Count}")]
    public abstract class Array<T> : IList<T>, IList, INotifyCollectionChanged, INotifyPropertyChanged
    {
        internal class DebugView
        {
            public DebugView(Array<T> arr)
            {
                Arr = arr;
            }
            Array<T> Arr;

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public T[] Items { get { return Arr.Inner.ToArray(); } }
        }

        protected List<T> Inner;
        protected ReaderWriterLockSlim RWLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        object _SyncRoot = new object();

        public virtual AttributeList Owner
        {
            get { return _Owner; }
            internal set { _Owner = value; OnPropertyChanged(); }
        }
        AttributeList _Owner;

        protected Datamodel OwnerDatamodel { get { return Owner == null ? null : Owner.Owner; } }

        internal Array()
        {
            Inner = new List<T>();
        }

        internal Array(IEnumerable<T> enumerable)
        {
            if (enumerable != null)
                Inner = new List<T>(enumerable);
            else
                Inner = new List<T>();
        }

        internal Array(int capacity)
        {
            Inner = new List<T>(capacity);
        }

        public int IndexOf(T item)
        {
            RWLock.EnterReadLock();
            try
            {
                return Inner.IndexOf(item);
            }
            finally
            {
                RWLock.ExitReadLock();
            }
        }

        public void Insert(int index, T item)
        {
            RWLock.EnterWriteLock();
            try
            {
                Insert_Internal(index, item);
            }
            finally
            {
                RWLock.ExitWriteLock();
            }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (object)item, index));
        }

        protected virtual void Insert_Internal(int index, T item)
        {
            Inner.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            object item;
            RWLock.EnterWriteLock();
            try
            {
                item = Inner[index];
                Inner.RemoveAt(index);
            }
            finally
            {
                RWLock.ExitWriteLock();
            }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
        }

        public virtual T this[int index]
        {
            get
            {
                RWLock.EnterReadLock();
                try
                {
                    return Inner[index];
                }
                finally
                {
                    RWLock.ExitReadLock();
                }
            }
            set
            {
                object current;
                RWLock.EnterUpgradeableReadLock();
                try
                {
                    current = Inner[index];
                    RWLock.EnterWriteLock();
                    try
                    {
                        Inner.RemoveAt(index);
                        Insert_Internal(index, value);
                    }
                    finally
                    {
                        RWLock.ExitWriteLock();
                    }
                }
                finally
                {
                    RWLock.ExitUpgradeableReadLock();
                }
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, current, (object)value, index));
            }
        }

        public void Add(T item)
        {
            Insert(Inner.Count, item);
        }

        public void Clear()
        {
            RWLock.EnterWriteLock();
            try
            {
                Inner.Clear();
            }
            finally
            {
                RWLock.ExitWriteLock();
            }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public bool Contains(T item)
        {
            RWLock.EnterReadLock();
            try
            {
                return Inner.Contains(item);
            }
            finally
            {
                RWLock.ExitReadLock();
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            CopyTo_Internal(array, arrayIndex);
        }

        protected virtual void CopyTo_Internal(Array array, int index)
        {
            RWLock.EnterUpgradeableReadLock();
            try
            {
                foreach (var item in this.Take(Math.Min(array.Length - index, Inner.Count)).ToArray())
                {
                    array.SetValue(item, index);
                    index++;
                }
            }
            finally
            {
                RWLock.ExitUpgradeableReadLock();
            }
        }

        public int Count
        {
            get
            {
                RWLock.EnterReadLock();
                try
                {
                    return Inner.Count;
                }
                finally
                {
                    RWLock.ExitReadLock();
                }
            }
        }

        bool ICollection<T>.IsReadOnly { get { return false; } }

        public bool Remove(T item)
        {
            int index;
            RWLock.EnterUpgradeableReadLock();
            try
            {
                index = Inner.IndexOf(item);
                if (index == -1) return false;

                RWLock.EnterWriteLock();
                try
                {
                    Inner.RemoveAt(index);
                }
                finally
                {
                    RWLock.ExitWriteLock();
                }
            }
            finally
            {
                RWLock.ExitUpgradeableReadLock();
            }

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, (object)item, index));
            return true;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Inner.Count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Reset:
                    OnPropertyChanged("Count");
                    break;
            }

            if (CollectionChanged != null)
                CollectionChanged(this, e);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName()] string property = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        #region IList

        int IList.Add(object value)
        {
            Add((T)value);
            return Count;
        }

        bool IList.Contains(object value)
        {
            return Contains((T)value);
        }

        int IList.IndexOf(object value)
        {
            return IndexOf((T)value);
        }

        void IList.Insert(int index, object value)
        {
            Insert(index, (T)value);
        }

        bool IList.IsFixedSize { get { return false; } }
        bool IList.IsReadOnly { get { return false; } }

        void IList.Remove(object value)
        {
            Remove((T)value);
        }

        object IList.this[int index]
        {
            get
            {
                return this[index];
            }
            set
            {
                this[index] = (T)value;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            CopyTo_Internal(array, index);
        }

        bool ICollection.IsSynchronized { get { return true; } }

        public object SyncRoot { get { return _SyncRoot; } }

        #endregion
    }

    public class ElementArray : Array<Element>
    {    
        public ElementArray() { }

        public ElementArray(IEnumerable<Element> enumerable)
            : base(enumerable)
        { }

        public ElementArray(int capacity)
            : base(capacity)
        { }

        /// <summary>
        /// Gets the values in the list without attempting destubbing.
        /// </summary>
        internal IEnumerable<Element> RawList { get { foreach (var elem in Inner) yield return elem; } }

        public override AttributeList Owner
        {
            get
            {
                return base.Owner;
            }
            internal set
            {
                RWLock.EnterUpgradeableReadLock();
                try
                {
                    base.Owner = value;

                    if (OwnerDatamodel != null)
                    {
                        for (int i = 0; i < Count; i++)
                        {
                            var elem = Inner[i];

                            if (elem == null) continue;
                            if (elem.Owner == null)
                            {
                                RWLock.EnterWriteLock();
                                try
                                {
                                    Inner[i] = OwnerDatamodel.ImportElement(elem, Datamodel.ImportRecursionMode.Stubs, Datamodel.ImportOverwriteMode.Stubs);
                                }
                                finally
                                {
                                    RWLock.ExitWriteLock();
                                }
                            }
                            else if (elem.Owner != OwnerDatamodel)
                                throw new ElementOwnershipException();
                        }
                    }
                }
                finally
                {
                    RWLock.ExitUpgradeableReadLock();
                }
            }
        }

        protected override void Insert_Internal(int index, Element item)
        {
            if (item != null && OwnerDatamodel != null)
            {
                if (item.Owner == null)
                    item = OwnerDatamodel.ImportElement(item, Datamodel.ImportRecursionMode.Recursive, Datamodel.ImportOverwriteMode.Stubs);
                else if (item.Owner != OwnerDatamodel)
                    throw new ElementOwnershipException();
            }

            base.Insert_Internal(index, item);
        }

        public override Element this[int index]
        {
            get
            {
                RWLock.EnterUpgradeableReadLock();
                try
                {
                    var elem = Inner[index];
                    if (elem != null && elem.Stub && elem.Owner != null)
                    {
                        RWLock.EnterWriteLock();
                        try
                        {
                            elem = Inner[index] = elem.Owner.OnStubRequest(elem.ID);
                        }
                        catch (Exception err)
                        {
                            throw new DestubException(this, index, err);
                        }
                        finally
                        {
                            RWLock.ExitWriteLock();
                        }
                    }
                    return elem;
                }
                finally
                {
                    RWLock.ExitUpgradeableReadLock();
                }
            }
            set
            {
                base[index] = value;
            }
        }
    }

    public class IntArray : Array<int>
    {
        public IntArray() { }
        public IntArray(IEnumerable<int> enumerable)
            : base(enumerable)
        { }
        public IntArray(int capacity)
            : base(capacity)
        { }
    }

    public class FloatArray : Array<float>
    {
        public FloatArray() { }
        public FloatArray(IEnumerable<float> enumerable)
            : base(enumerable)
        { }
        public FloatArray(int capacity)
            : base(capacity)
        { }
    }

    public class BoolArray : Array<bool>
    {
        public BoolArray() { }
        public BoolArray(IEnumerable<bool> enumerable)
            : base(enumerable)
        { }
        public BoolArray(int capacity)
            : base(capacity)
        { }
    }

    public class StringArray : Array<string>
    {
        public StringArray() { }
        public StringArray(IEnumerable<string> enumerable)
            : base(enumerable)
        { }
        public StringArray(int capacity)
            : base(capacity)
        { }
    }

    public class BinaryArray : Array<byte[]>
    {
        public BinaryArray() { }
        public BinaryArray(IEnumerable<byte[]> enumerable)
            : base(enumerable)
        { }
        public BinaryArray(int capacity)
            : base(capacity)
        { }
    }

    public class TimeSpanArray : Array<TimeSpan>
    {
        public TimeSpanArray() { }
        public TimeSpanArray(IEnumerable<TimeSpan> enumerable)
            : base(enumerable)
        { }
        public TimeSpanArray(int capacity)
            : base(capacity)
        { }
    }

    public class ColorArray : Array<Color>
    {
        public ColorArray() { }
        public ColorArray(IEnumerable<Color> enumerable)
            : base(enumerable)
        { }
        public ColorArray(int capacity)
            : base(capacity)
        { }
    }

    public class Vector2Array : Array<Vector2>
    {
        public Vector2Array() { }
        public Vector2Array(IEnumerable<Vector2> enumerable)
            : base(enumerable)
        { }
        public Vector2Array(int capacity)
            : base(capacity)
        { }
    }

    public class Vector3Array : Array<Vector3>
    {
        public Vector3Array() { }
        public Vector3Array(IEnumerable<Vector3> enumerable)
            : base(enumerable)
        { }
        public Vector3Array(int capacity)
            : base(capacity)
        { }
    }

    public class Vector4Array : Array<Vector4>
    {
        public Vector4Array() { }
        public Vector4Array(IEnumerable<Vector4> enumerable)
            : base(enumerable)
        { }
        public Vector4Array(int capacity)
            : base(capacity)
        { }
    }

    public class QuaternionArray : Array<Quaternion>
    {
        public QuaternionArray() { }
        public QuaternionArray(IEnumerable<Quaternion> enumerable)
            : base(enumerable)
        { }
        public QuaternionArray(int capacity)
            : base(capacity)
        { }
    }

    public class MatrixArray : Array<Matrix4x4>
    {
        public MatrixArray() { }
        public MatrixArray(IEnumerable<Matrix4x4> enumerable)
            : base(enumerable)
        { }
        public MatrixArray(int capacity)
            : base(capacity)
        { }
    }

    public class ByteArray : Array<byte>
    {
        public ByteArray() { }
        public ByteArray(IEnumerable<byte> enumerable)
            : base(enumerable)
        { }
        public ByteArray(int capacity)
            : base(capacity)
        { }
    }

    [CLSCompliant(false)]
    public class UInt64Array : Array<UInt64>
    {
        public UInt64Array() { }
        public UInt64Array(IEnumerable<UInt64> enumerable)
            : base(enumerable)
        { }
        public UInt64Array(int capacity)
            : base(capacity)
        { }
    }
}
