using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Drawing;

namespace Datamodel
{
    public abstract class Array<T> : IList<T>, IList, INotifyCollectionChanged
    {
        List<T> Inner;
        System.Threading.ReaderWriterLockSlim RWLock = new System.Threading.ReaderWriterLockSlim();
        object _SyncRoot = new object();

        public virtual Element Owner { get; internal set; }
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

        public T this[int index]
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
                    current = this[index];
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
            RWLock.EnterReadLock();
            try
            {
                Inner.CopyTo(array, arrayIndex);
            }
            finally
            {
                RWLock.ExitReadLock();
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
            return Inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Inner.GetEnumerator();
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (CollectionChanged != null)
                CollectionChanged(this, e);
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
            RWLock.EnterReadLock();
            try
            {
                ((IList)Inner).CopyTo(array, index);
            }
            finally
            {
                RWLock.ExitReadLock();
            }
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

        public override Element Owner
        {
            get
            {
                return base.Owner;
            }
            internal set
            {
                base.Owner = value;

                if (OwnerDatamodel != null)
                {
                    foreach (var elem in this)
                    {
                        if (elem == null) continue;
                        if (elem.Owner == null)
                            OwnerDatamodel.ImportElement(elem, true, false);
                        else if (elem.Owner != OwnerDatamodel)
                            throw new ElementOwnershipException();
                    }
                }
            }
        }

        protected override void Insert_Internal(int index, Element item)
        {
            base.Insert_Internal(index, item);

            if (item != null && OwnerDatamodel != null)
            {
                if (item.Owner == null)
                    OwnerDatamodel.ImportElement(item, true, false);
                else if (item.Owner != OwnerDatamodel)
                    throw new ElementOwnershipException();
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

    public class AngleArray : Array<Angle>
    {
        public AngleArray() { }
        public AngleArray(IEnumerable<Angle> enumerable)
            : base(enumerable)
        { }
        public AngleArray(int capacity)
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

    public class MatrixArray : Array<Matrix>
    {
        public MatrixArray() { }
        public MatrixArray(IEnumerable<Matrix> enumerable)
            : base(enumerable)
        { }
        public MatrixArray(int capacity)
            : base(capacity)
        { }
    }
}
