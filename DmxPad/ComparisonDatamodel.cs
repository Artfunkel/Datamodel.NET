using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

using Datamodel;

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
                    || !Enumerable.SequenceEqual(Element_Left.Select(a => a.Name), Element_Right.Select(a => a.Name)))
                    State = ComparisonState.Changed;

                if (Element_Right != null)
                {
                    Owner.ComparedElements[Element_Right.ID] = this;
                    foreach (var attr_right in Element_Right.Where(a => Element_Left != null && !Element_Left.Contains(a.Name)))
                        Attributes.Add(attr_right.Name, new Attribute(this, null, attr_right));
                }

                if (Element_Left != null)
                {
                    Owner.ComparedElements[Element_Left.ID] = this;
                    foreach (var attr_left in Element_Left)
                    {
                        Datamodel.Attribute attr_right = null;
                        if (Element_Right != null && Element_Right.Contains(attr_left.Name))
                            attr_right = Element_Right.GetAttribute(attr_left.Name);
                        Attributes.Add(attr_left.Name, new Attribute(this, attr_left, attr_right));
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

            public Datamodel.Attribute Attribute_Left
            {
                get { return _Attribute_Left; }
                set { _Attribute_Left = value; }
            }
            Datamodel.Attribute _Attribute_Left;

            public Datamodel.Attribute Attribute_Right
            {
                get { return _Attribute_Right; }
                set { _Attribute_Right = value; }
            }
            Datamodel.Attribute _Attribute_Right;

            public object Value_Combined
            {
                get { return _Value_Combined; }
                protected set { _Value_Combined = value; }
            }
            object _Value_Combined;

            #endregion

            public Attribute(Element owner, Datamodel.Attribute attr_left, Datamodel.Attribute attr_right)
            {
                Owner = owner;
                Attribute_Left = attr_left;
                Attribute_Right = attr_right;

                var cdm = Owner.Owner;

                Name = Attribute_Left != null ? Attribute_Left.Name : Attribute_Right.Name;

                if (Attribute_Left == null)
                {
                    State = ComparisonState.Added;
                    Value_Combined = Attribute_Right.Value;
                }
                else if (Attribute_Right == null)
                {
                    State = ComparisonState.Removed;
                    Value_Combined = Attribute_Left.Value;
                }
                else
                {
                    if (!Datamodel.Attribute.ValueComparer.Default.Equals(Attribute_Left, Attribute_Right))
                        State = ComparisonState.Changed;

                    if (Attribute_Left.Value == null)
                        Value_Combined = Attribute_Right.Value;
                    else if (Attribute_Right.Value == null)
                        Value_Combined = Attribute_Left.Value;
                    else
                    {
                        if (Attribute_Left.Value.GetType() == typeof(Datamodel.Element))
                            Value_Combined = new Element(cdm, (Datamodel.Element)Attribute_Left.Value, (Datamodel.Element)Attribute_Right.Value);
                        else
                        {
                            var inner = Datamodel.Datamodel.GetArrayInnerType(Attribute_Left.Value.GetType());
                            if (inner == typeof(Datamodel.Element))
                                Value_Combined = ((IList<Datamodel.Element>)Attribute_Left.Value)
                                    .Concat((IList<Datamodel.Element>)Attribute_Right.Value)
                                    .Distinct(Datamodel.Element.IDComparer.Default)
                                    .Select(e => new Element(cdm, cdm.Datamodel_Left.AllElements[e.ID], cdm.Datamodel_Right.AllElements[e.ID])).ToArray();
                            else
                                Value_Combined = Attribute_Right.Value;
                        }
                    }
                }
            }
        }
    }
}
