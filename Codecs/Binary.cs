using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Datamodel.Codecs
{
    class Binary : IDeferredAttributeCodec, IDisposable
    {
        protected BinaryReader Reader;
        protected BinaryWriter Writer;

        static readonly Dictionary<int, Type[]> SupportedAttributes = new Dictionary<int, Type[]>();

        static Binary()
        {
            SupportedAttributes[1] = SupportedAttributes[2] = SupportedAttributes[3] = new Type[] { typeof(Element), typeof(int), typeof(float), typeof(bool), typeof(string), typeof(byte[]), null /* ObjectID */, typeof(System.Drawing.Color), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Angle), typeof(Quaternion), typeof(Matrix) };
            SupportedAttributes[5] = new Type[] { typeof(Element), typeof(int), typeof(float), typeof(bool), typeof(string), typeof(byte[]), typeof(TimeSpan), typeof(System.Drawing.Color), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Angle), typeof(Quaternion), typeof(Matrix) };
        }

        public void Dispose()
        {
            if (Reader != null) Reader.Dispose();
            if (Writer != null) Writer.Dispose();
        }

        static byte TypeToId(Type type, int version)
        {
            bool array = Datamodel.IsDatamodelArrayType(type);
            var search_type = array ? Datamodel.GetArrayInnerType(type) : type;

            var type_list = SupportedAttributes[version];
            byte i = 0;
            foreach (var list_type in type_list)
            {
                if (list_type == search_type) break;
                else i++;
            }
            if (i == type_list.Length)
                throw new CodecException(String.Format("{0} is not supported in encoding binary {1}", type.Name, version));
            if (array) i += (byte)type_list.Length;
            return ++i;
        }

        static Type IdToType(byte id, int version)
        {
            id--;
            var type_list = SupportedAttributes[version];

            if (id >= type_list.Length * 2)
                throw new CodecException("Unrecognised attribute type: " + id);

            bool array = false;
            if (id >= type_list.Length)
            {
                id -= (byte)(type_list.Length);
                array = true;
            }
            return array ? type_list[id].MakeListType() : type_list[id];
        }

        protected StringBuilder StringBuilder = new StringBuilder();
        protected string ReadString_Raw()
        {
            StringBuilder.Clear();
            while (true)
            {
                char c = Reader.ReadChar();
                if (c == 0) break;
                StringBuilder.Append(c);
            }
            return StringBuilder.ToString();
        }
        protected void WriteString_Raw(string value)
        {
            Writer.Write(value.Select(c => (byte)c).ToArray());
            Writer.Write((byte)0);
        }

        class StringDictionary
        {
            Binary Codec;
            List<string> Strings = new List<string>();
            public bool UseShorts;
            public bool Dummy;

            public StringDictionary(Binary codec, int encoding_version, BinaryReader reader)
            {
                Codec = codec;
                Dummy = encoding_version == 1;
                if (!Dummy)
                {
                    UseShorts = encoding_version < 5;

                    int dict_size = ReadNumber();

                    foreach (var i in Enumerable.Range(0, dict_size))
                        Strings.Add(Codec.ReadString_Raw());
                }
            }

            public StringDictionary(Binary codec, int encoding_version, Datamodel dm)
            {
                Codec = codec;
                Dummy = encoding_version == 1;
                if (!Dummy)
                {
                    UseShorts = encoding_version < 5;
                    Action<Element> ScrapeElement = null;
                    ScrapeElement = elem =>
                        {
                            if (elem.Stub) return;
                            var scraped = new List<Element>();
                            Strings.Add(elem.Name);
                            Strings.Add(elem.ClassName);
                            foreach (var attr in elem)
                            {
                                Strings.Add(attr.Name);
                                if (attr.Value is string) Strings.Add((string)attr.Value);
                                if (attr.Value is Element) ScrapeElement((Element)attr.Value);
                                if (attr.Value is IList<Element>)
                                    foreach (var array_elem in (IList<Element>)attr.Value)
                                        ScrapeElement(array_elem);
                            }
                        };

                    ScrapeElement(dm.Root);
                    Strings = Strings.Distinct().ToList();
                }
            }

            int ReadNumber()
            {
                return UseShorts ? Codec.Reader.ReadInt16() : Codec.Reader.ReadInt32();
            }

            public string ReadString()
            {
                if (Dummy) return Codec.ReadString_Raw();
                else return Strings[ReadNumber()];
            }

            public void WriteString(string value)
            {
                if (Dummy)
                    Codec.WriteString_Raw(value);
                else
                {
                    var index = Strings.IndexOf(value);
                    if (UseShorts) Codec.Writer.Write((short)index);
                    else Codec.Writer.Write(index);
                }
            }

            public void WriteSelf()
            {
                if (Dummy) return;

                if (UseShorts) Codec.Writer.Write((short)Strings.Count);
                else Codec.Writer.Write(Strings.Count);

                foreach (var str in Strings)
                    Codec.WriteString_Raw(str);
            }
        }
        StringDictionary StringDict;

        public void Encode(Datamodel dm, int encoding_version, Stream stream)
        {
            Writer = new BinaryWriter(stream);

            WriteString_Raw(String.Format(CodecUtilities.HeaderPattern, "binary", encoding_version, dm.Format, dm.FormatVersion) + "\n");

            var dict = new StringDictionary(this, encoding_version, dm);
            var elem_order = new List<Element>();

            Action<Element> WriteIndex = null;
            WriteIndex = elem =>
                {
                    elem_order.Add(elem);
                    dict.WriteString(elem.ClassName);
                    if (encoding_version >= 5) dict.WriteString(elem.Name);
                    else WriteString_Raw(elem.Name);
                    Writer.Write(elem.ID.ToByteArray());

                    foreach (var attr in elem)
                    {
                        var child_elem = attr.Value as Element;
                        if (child_elem != null)
                        {
                            if (!elem_order.Contains(child_elem))
                                WriteIndex(child_elem);
                        }
                        else
                        {
                            var elem_list = attr.Value as IList<Element>;
                            if (elem_list != null)
                                foreach (var item in elem_list)
                                    if (!elem_order.Contains(item))
                                        WriteIndex(item);
                        }
                    }
                };

            Action<Element> WriteBody = elem =>
            {
                if (elem.Stub)
                {
                    if (encoding_version < 5)
                        Writer.Write(-1);
                    else
                    {
                        Writer.Write(-2);
                        Writer.Write(elem.ID.ToString().ToArray()); // yes, ToString()!
                        Writer.Write((byte)0);
                    }
                    return;
                }
                Writer.Write(elem.Count);
                foreach (var attr in elem)
                {
                    dict.WriteString(attr.Name);
                    var attr_type = attr.Value == null ? typeof(Element) : attr.Value.GetType();
                    Writer.Write(TypeToId(attr_type, encoding_version));

                    Action<object, bool> WriteValue = (out_value, in_array) =>
                        {
                            if (attr_type == typeof(Element))
                            {
                                var child_elem = (Element)out_value;
                                if (child_elem == null)
                                    Writer.Write(-1);
                                else if (child_elem.Stub)
                                {
                                    Writer.Write(-2);
                                    byte[] bits = child_elem.ID.ToByteArray();
                                    Writer.Write(BitConverter.IsLittleEndian ? bits : bits.Reverse().ToArray());
                                }
                                else
                                    Writer.Write(elem_order.IndexOf(child_elem));
                                return;
                            }

                            if (attr_type == typeof(string))
                            {
                                if (encoding_version < 5 || in_array)
                                    WriteString_Raw((string)out_value);                                    
                                else
                                    dict.WriteString((string)out_value);
                                return;
                            }

                            if (attr_type == typeof(bool))
                                out_value = (bool)out_value ? (byte)1 : (byte)0;

                            else if (attr_type == typeof(byte[]))
                                Writer.Write(((byte[])out_value).Length);

                            else if (attr_type == typeof(TimeSpan))
                                out_value = (int)(((TimeSpan)out_value).TotalSeconds * 10000);

                            else if (attr_type == typeof(System.Drawing.Color))
                            {
                                var color = (System.Drawing.Color)out_value;
                                out_value = new byte[] { color.R, color.G, color.B, color.A };
                            }
                            else if (attr_type.IsSubclassOf(typeof(VectorBase)))
                                out_value = ((VectorBase)out_value).SelectMany(f => BitConverter.GetBytes(f)).ToArray();

                            Writer.GetType().GetMethod("Write", new Type[] { out_value.GetType() }).Invoke(Writer, new object[] { out_value });
                        };

                    if (attr.Value == null || !Datamodel.IsDatamodelArrayType(attr.Value.GetType()))
                        WriteValue(attr.Value, false);
                    else
                    {
                        var array = (System.Collections.IList)attr.Value;
                        Writer.Write(array.Count);
                        attr_type = Datamodel.GetArrayInnerType(array.GetType());
                        foreach (var item in array)
                            WriteValue(item, true);
                    }
                }
            };


            dict.WriteSelf();

            Func<Element, int> CountChildElems = null;
            var counted = new List<Element>();
            CountChildElems = (elem) =>
                {
                    int num_elems = 1;
                    counted.Add(elem);
                    foreach (var child in elem.Select(a => a.Value))
                    {
                        if (child == null) continue;
                        else if (child.GetType() == typeof(Element) && !counted.Contains(child))
                            num_elems += CountChildElems(child as Element);
                        else if (child.GetType() == typeof(List<Element>))
                            foreach (var child_elem in (child as List<Element>).Where(c => c != null && !counted.Contains(c)))
                                num_elems += CountChildElems(child_elem);
                    }
                    return num_elems;
                };

            Writer.Write(CountChildElems(dm.Root));

            WriteIndex(dm.Root);
            elem_order.ForEach(e => WriteBody(e));
        }

        object ReadValue(Datamodel dm, Type type, bool raw_string)
        {
            if (type == typeof(Element))
            {
                var index = Reader.ReadInt32();
                if (index == -1)
                    return null;
                else if (index == -2)
                    return dm.CreateStubElement(new Guid(ReadString_Raw())); // yes, it's in ASCII!
                else
                    return dm.AllElements[index];
            }
            if (type == typeof(int))
                return Reader.ReadInt32();
            if (type == typeof(float))
                return Reader.ReadSingle();
            if (type == typeof(bool))
                return Reader.ReadBoolean();
            if (type == typeof(string))
                return raw_string ? ReadString_Raw() : StringDict.ReadString();

            if (type == typeof(byte[]))
                return (Reader.ReadBytes(Reader.ReadInt32()));
            if (type == typeof(TimeSpan))
                return TimeSpan.FromSeconds(Reader.ReadInt32() / 10000f);

            if (type == typeof(System.Drawing.Color))
            {
                var rgba = Reader.ReadBytes(4);
                return System.Drawing.Color.FromArgb(rgba[3], rgba[0], rgba[1], rgba[2]);
            }

            Func<int, float[]> ReadVector = dim =>
                {
                    var output = new float[dim];
                    foreach (int i in Enumerable.Range(0, dim))
                        output[i] = Reader.ReadSingle();
                    return output;
                };

            if (type == typeof(Vector2))
                return new Vector2(ReadVector(2));
            if (type == typeof(Vector3))
                return new Vector3(ReadVector(3));
            if (type == typeof(Angle))
                return new Angle(ReadVector(3));
            if (type == typeof(Vector4))
                return new Vector4(ReadVector(4));
            if (type == typeof(Quaternion))
                return new Quaternion(ReadVector(4));
            if (type == typeof(Matrix))
                return new Matrix(ReadVector(4 * 4));

            throw new ArgumentException(type == null ? "No type provided to GetValue()" : "Cannot read value of type " + type.Name);
        }

        public Datamodel Decode(int encoding_version, string format, int format_version, Stream stream, DeferredMode defer_mode)
        {
            stream.Seek(0, SeekOrigin.Begin);
            while (true)
            {
                var b = stream.ReadByte();
                if (b == 0) break;
            }
            var dm = new Datamodel(format, format_version);

            EncodingVersion = encoding_version;
            Reader = new BinaryReader(stream, Encoding.ASCII);
            StringDict = new StringDictionary(this, EncodingVersion, Reader);

            var num_elements = Reader.ReadInt32();

            // read index
            foreach (var i in Enumerable.Range(0, num_elements))
            {
                var type = StringDict.ReadString();
                var name = EncodingVersion >= 5 ? StringDict.ReadString() : ReadString_Raw();
                var id_bits = Reader.ReadBytes(16);
                var id = new Guid(BitConverter.IsLittleEndian ? id_bits : id_bits.Reverse().ToArray());
                dm.CreateElement(name, id, type);
            }

            // read attributes (or not, if we're deferred)
            foreach (var elem in dm.AllElements)
            {
                var num_attrs = Reader.ReadInt32();

                foreach (var i in Enumerable.Range(0, num_attrs))
                {
                    var name = StringDict.ReadString();

                    if (defer_mode == DeferredMode.Automatic)
                    {
                        CodecUtilities.AddAttribute(elem, name, null, Reader.BaseStream.Position);
                        SkipAttribte();
                    }
                    else
                    {
                        CodecUtilities.AddAttribute(elem, name, DecodeAttribute(dm), 0);
                    }
                }
            }
            return dm;
        }

        int EncodingVersion;

        public object DeferredDecodeAttribute(Datamodel dm, long offset)
        {
            Reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            return DecodeAttribute(dm);
        }

        object DecodeAttribute(Datamodel dm)
        {
            var type = IdToType(Reader.ReadByte(), EncodingVersion);

            if (!Datamodel.IsDatamodelArrayType(type))
                return ReadValue(dm, type, EncodingVersion < 5);
            else
            {
                var inner_type = Datamodel.GetArrayInnerType(type);
                var array = type.GetConstructor(Type.EmptyTypes).Invoke(null);
                var add = type.GetMethod("Add", new Type[] { inner_type });

                foreach (var x in Enumerable.Range(0, Reader.ReadInt32()))
                    add.Invoke(array, new object[] { ReadValue(dm, inner_type, true) });

                return array;
            }
        }

        void SkipAttribte()
        {
            var type = IdToType(Reader.ReadByte(), EncodingVersion);

            int count = 1;
            bool array = false;
            if (Datamodel.IsDatamodelArrayType(type))
            {
                array = true;
                count = Reader.ReadInt32();
                type = Datamodel.GetArrayInnerType(type);
            }

            if (type == typeof(Element))
            {
                foreach (int i in Enumerable.Range(0, count))
                    if (Reader.ReadInt32() == -2) Reader.BaseStream.Seek(37, SeekOrigin.Current); // skip GUID + null terminator if a stub
                return;
            }

            int length;

            if (type == typeof(TimeSpan))
                length = sizeof(int);
            else if (type == typeof(System.Drawing.Color))
                length = 4;
            else if (type == typeof(bool))
                length = 1;
            else if (type == typeof(byte[]))
            {
                foreach (var i in Enumerable.Range(0, count))
                    Reader.BaseStream.Seek(Reader.ReadInt32(), SeekOrigin.Current);
                return;
            }
            else if (type == typeof(string))
            {
                if (!StringDict.Dummy && !array && EncodingVersion >= 5)
                    length = StringDict.UseShorts ? sizeof(short) : sizeof(int);
                else
                {
                    foreach (var i in Enumerable.Range(0, count))
                    {
                        byte b;
                        do { b = Reader.ReadByte(); } while (b != 0);
                    }
                    return;
                }
            }
            else if (type == typeof(Vector2))
                length = sizeof(float) * 2;
            else if (type == typeof(Vector3) || type == typeof(Angle))
                length = sizeof(float) * 3;
            else if (type == typeof(Vector4) || type == typeof(Quaternion))
                length = sizeof(float) * 4;
            else if (type == typeof(Matrix))
                length = sizeof(float) * 4 * 4;
            else
                length = System.Runtime.InteropServices.Marshal.SizeOf(type);

            Reader.BaseStream.Seek(length * count, SeekOrigin.Current);
        }
    }
}
