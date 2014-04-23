using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

using Datamodel;
using AttrKVP = System.Collections.Generic.KeyValuePair<string, object>;

namespace DmxPad
{
    public class ComparisonDatamodel
    {
        #region Properties

        public Datamodel.Datamodel Datamodel_Left
        {
            get { return _Datamodel_Left; }
            private set { _Datamodel_Left = value; }
        }
        Datamodel.Datamodel _Datamodel_Left;

        public Datamodel.Datamodel Datamodel_Right
        {
            get { return _Datamodel_Right; }
            private set { _Datamodel_Right = value; }
        }
        Datamodel.Datamodel _Datamodel_Right;

        public Dictionary<Guid, Element> ComparedElements { get { return _ComparedElements; } }
        Dictionary<Guid, Element> _ComparedElements = new Dictionary<Guid, Element>();

        public Element Root
        {
            get { return _Root; }
            protected set { _Root = value; }
        }
        Element _Root;

        #endregion

        public ComparisonDatamodel(Datamodel.Datamodel dm_left, Datamodel.Datamodel dm_right)
        {
            Datamodel_Left = dm_left;
            Datamodel_Right = dm_right;
            Root = new ComparisonDatamodel.Element(this, Datamodel_Left.Root, Datamodel_Right.Root);
        }

        public enum ComparisonState
        {
            Unchanged,
            ChildChanged,
            Changed,
            Added,
            Removed,
        }

        public interface IComparisonItem
        {
            ComparisonState State { get; }
        }

        public class Element : IComparisonItem, IEnumerable<Attribute>
        {
            #region Properties
            public ComparisonDatamodel Owner
            {
                get { return _Owner; }
                protected set { _Owner = value; }
            }
            ComparisonDatamodel _Owner;

            public ComparisonState State
            {
                get
                {
                    var state = _State;
                    if (state == ComparisonState.Unchanged && this.Any(a => a.State != ComparisonState.Unchanged))
                        state = ComparisonState.ChildChanged;
                    return state;
                }
                protected set { _State = value; }
            }
            ComparisonState _State = ComparisonState.Unchanged;

            public Datamodel.Element Element_Left
            {
                get { return _Element_Left; }
                protected set { _Element_Left = value; }
            }
            Datamodel.Element _Element_Left;

            public Datamodel.Element Element_Right
            {
                get { return _Element_Right; }
                protected set { _Element_Right = value; }
            }
            Datamodel.Element _Element_Right;

            OrderedDictionary Attributes = new OrderedDictionary();

            public string ClassName
            {
                get { return Element_Right != null ? Element_Right.ClassName : Element_Left.ClassName; }
            }
            #endregion

            public static readonly object NoAttributeValue = new object();

            public Element(ComparisonDatamodel owner, Datamodel.Element elem_left, Datamodel.Element elem_right)
            {
                Owner = owner;
                Element_Left = elem_left;
                Element_Right = elem_right;

                if (Element_Left == null)
                    State = ComparisonState.Added;
                else if (Element_Right == null)
                    State = ComparisonState.Removed;
                else if (Element_Left.Name != Element_Right.Name ||
                    Element_Left.ID != Element_Right.ID ||
                    Element_Left.ClassName != Element_Right.ClassName ||
                    Element_Left.Stub != Element_Right.Stub
                    || !Enumerable.SequenceEqual(Element_Left.Select(a => a.Key), Element_Right.Select(a => a.Key)))
                    State = ComparisonState.Changed;

                if (Element_Right != null)
                {
                    Owner.ComparedElements[Element_Right.ID] = this;
                    foreach (var attr_right in Element_Right.Where(a => Element_Left != null && !Element_Left.ContainsKey(a.Key)))
                        Attributes.Add(attr_right.Key, new Attribute(this,attr_right.Key, NoAttributeValue, attr_right.Value));
                }

                if (Element_Left != null)
                {
                    Owner.ComparedElements[Element_Left.ID] = this;
                    foreach (var attr_left in Element_Left)
                    {
                        object value_right = NoAttributeValue;
                        if (Element_Right != null && Element_Right.ContainsKey(attr_left.Key))
                            value_right = Element_Right[attr_left.Key];
                        Attributes.Add(attr_left.Key, new Attribute(this, attr_left.Key, attr_left.Value, value_right));
                    }
                }
            }

            public IEnumerator<Attribute> GetEnumerator()
            {
                foreach (var attr in Attributes)
                {
                    var entry = (DictionaryEntry)attr;
                    yield return (Attribute)entry.Value;
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return (IEnumerator)GetEnumerator();
            }
        }

        public class Attribute : IComparisonItem
        {
            #region Properties
            public Element Owner
            {
                get { return _Owner; }
                protected set { _Owner = value; }
            }
            Element _Owner;

            public string Name
            {
                get { return _Name; }
                protected set { _Name = value; }
            }
            string _Name;

            public ComparisonState State
            {
                get
                {
                    var state = _State;
                    if (state == ComparisonState.Unchanged)
                    {
                        var elem = Value_Combined as Element;
                        if (elem != null)
                        {
                            if (elem.State != ComparisonState.Unchanged)
                                state = ComparisonState.ChildChanged;
                        }
                        else
                        {
                            var array = Value_Combined as IList<Element>;
                            if (array != null && array.Any(e => e.State != ComparisonState.Unchanged))
                                state = ComparisonState.ChildChanged;
                        }
                    }
                    return state;
                }
                protected set { _State = value; }
            }
            ComparisonState _State = ComparisonState.Unchanged;

            public object Value_Left { get; set; }
            public object Value_Right { get; set; }

            public object Value_Combined
            {
                get { return _Value_Combined; }
                protected set { _Value_Combined = value; }
            }
            object _Value_Combined;

            #endregion

            public Attribute(Element owner, string name, object value_left, object value_right)
            {
                Owner = owner;
                Value_Left = value_left;
                Value_Right = value_right;

                var cdm = Owner.Owner;

                Name = name;

                if (value_left == Element.NoAttributeValue)
                {
                    State = ComparisonState.Added;
                    Value_Combined = value_right;
                }
                else if (value_right == Element.NoAttributeValue)
                {
                    State = ComparisonState.Removed;
                    Value_Combined = value_left;
                }
                else
                {
                    //if (!Datamodel.Attribute.ValueComparer.Default.Equals(attr_left, attr_right))
                    if (!object.Equals(value_left,value_right))
                        State = ComparisonState.Changed;

                    if (Value_Left == null)
                        Value_Combined = Value_Right;
                    else if (Value_Right == null)
                        Value_Combined = Value_Left;
                    else
                    {
                        if (Value_Left.GetType() == typeof(Datamodel.Element))
                            Value_Combined = new Element(cdm, (Datamodel.Element)Value_Left, (Datamodel.Element)Value_Right);
                        else
                        {
                            var inner = Datamodel.Datamodel.GetArrayInnerType(Value_Left.GetType());
                            if (inner == typeof(Datamodel.Element))
                                Value_Combined = ((IList<Datamodel.Element>)Value_Left)
                                    .Concat((IList<Datamodel.Element>)Value_Right)
                                    .Distinct(Datamodel.Element.IDComparer.Default)
                                    .Select(e => new Element(cdm, cdm.Datamodel_Left.AllElements[e.ID], cdm.Datamodel_Right.AllElements[e.ID])).ToArray();
                            else
                                Value_Combined = Value_Right;
                        }
                    }
                }
            }
        }
    }
}
