using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Datamodel
{
    public partial class AttributeList
    {
        #region Boring pass-throughs
        AttributeCollection ICustomTypeDescriptor.GetAttributes()
        {
            return new AttributeCollection();
        }

        string ICustomTypeDescriptor.GetClassName()
        {
            return null;
        }

        string ICustomTypeDescriptor.GetComponentName()
        {
            return null;
        }

        TypeConverter ICustomTypeDescriptor.GetConverter()
        {
            return null;
        }

        EventDescriptor ICustomTypeDescriptor.GetDefaultEvent()
        {
            return null;
        }

        PropertyDescriptor ICustomTypeDescriptor.GetDefaultProperty()
        {
            return null;
        }

        object ICustomTypeDescriptor.GetEditor(Type editorBaseType)
        {
            return null;
        }

        EventDescriptorCollection ICustomTypeDescriptor.GetEvents()
        {
            return new EventDescriptorCollection(null);
        }

        EventDescriptorCollection ICustomTypeDescriptor.GetEvents(System.Attribute[] attributes)
        {
            return new EventDescriptorCollection(null);
        }

        public PropertyDescriptorCollection GetProperties()
        {
            return ((ICustomTypeDescriptor)this).GetProperties(null);
        }
#endregion

        class AttributePropertyDescriptor : PropertyDescriptor
        {
            private Type AttributeType;
            public AttributePropertyDescriptor(string key, Type type) : base(key, null)
            {
                AttributeType = type;
            }

            public override Type ComponentType { get { return typeof(AttributeList); } }
            public override bool IsReadOnly { get { return false; } }
            public override Type PropertyType { get { return AttributeType; } }

            public override bool CanResetValue(object component)
            {
                return true;
            }

            public override object GetValue(object component)
            {
                return ((AttributeList)component)[Name];
            }

            public override void ResetValue(object component)
            {
                ((AttributeList)component)[Name] = AttributeType.IsValueType ? Activator.CreateInstance(AttributeType) : null;
            }

            public override void SetValue(object component, object value)
            {
                ((AttributeList)component)[Name] = value;
            }

            public override bool ShouldSerializeValue(object component)
            {
                return false;
            }
        }

        public PropertyDescriptorCollection GetProperties(System.Attribute[] attributes)
        {
            var properties = TypeDescriptor.GetProperties(GetType(), attributes).Cast<PropertyDescriptor>().ToList();
            var reserved_names = new HashSet<string>(properties.Select(pd => pd.Name));

            foreach (Attribute attr in Inner.Values)
            {
                if (reserved_names.Contains(attr.Name))
                    continue; // you'll have to use the "this[key]" binding syntax for these.

                properties.Add(new AttributePropertyDescriptor(attr.Name, attr.ValueType));
            }

            return new PropertyDescriptorCollection(properties.ToArray());
        }

        object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor pd)
        {
            return this;
        }
    }
}
