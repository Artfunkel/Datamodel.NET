using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;
using System.IO;

namespace Datamodel.Codecs
{
    [CodecFormat("binary", 1)]
    [CodecFormat("binary", 2)]
    [CodecFormat("binary", 3)]
    [CodecFormat("binary", 4)]
    [CodecFormat("binary", 5)]
    [CodecFormat("binary", 9)]
    class Binary : IDeferredAttributeCodec, IDisposable
    {
        protected BinaryReader Reader;

        static readonly Dictionary<int, Type[]> SupportedAttributes = new Dictionary<int, Type[]>();

        static Binary()
        {
            SupportedAttributes[1] = SupportedAttributes[2] = new Type[] { typeof(Element), typeof(int), typeof(float), typeof(bool), typeof(string), typeof(byte[]), null /* ObjectID */, typeof(System.Drawing.Color), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Vector3) /* angle*/, typeof(Quaternion), typeof(Matrix4x4) };
            SupportedAttributes[3] = SupportedAttributes[4] = SupportedAttributes[5] = new Type[] { typeof(Element), typeof(int), typeof(float), typeof(bool), typeof(string), typeof(byte[]), typeof(TimeSpan), typeof(System.Drawing.Color), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Vector3) /* angle*/, typeof(Quaternion), typeof(Matrix4x4) };
            SupportedAttributes[9] = new Type[] { typeof(Element), typeof(int), typeof(float), typeof(bool), typeof(string), typeof(byte[]), typeof(TimeSpan), typeof(System.Drawing.Color), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Vector3) /* angle*/, typeof(Quaternion), typeof(Matrix4x4), typeof(UInt64), typeof(byte) };
        }

        public void Dispose()
        {
            if (Reader != null) Reader.Dispose();
        }

        static byte TypeToId(Type type, int version)
        {
            bool array = Datamodel.IsDatamodelArrayType(type);
            var search_type = array ? Datamodel.GetArrayInnerType(type) : type;

            if (array && search_type == typeof(byte) && !SupportedAttributes[version].Contains(typeof(byte)))
            {
                search_type = typeof(byte[]); // Recent version of DMX support both "binary" and "uint8_array" attributes. These are the same thing!
                array = false;
            }
            var type_list = SupportedAttributes[version];
            byte i = 0;
            foreach (var list_type in type_list)
            {
                if (list_type == search_type) break;
                else i++;
            }
            if (i == type_list.Length)
                throw new CodecException(String.Format("\"{0}\" is not supported in encoding binary {1}", type.Name, version));
            if (array) i += (byte)(type_list.Length * (version >= 9 ? 2 : 1));
            return ++i;
        }

        Type IdToType(byte id)
        {
            var type_list = SupportedAttributes[EncodingVersion];
            bool array = false;

            id--;

            if (EncodingVersion >= 9 && id >= type_list.Length * 2)
            {
                array = true;
                id -= (byte)(type_list.Length * 2);
            }
            else
            {
                if (id >= type_list.Length)
                {
                    id -= (byte)(type_list.Length);
                    array = true;
                }
            }

            try
            {
                return array ? type_list[id].MakeListType() : type_list[id];
            }
            catch (IndexOutOfRangeException)
            {
                throw new CodecException(String.Format("Unrecognised attribute type: {0}", id + 1));
            }
        }

        protected string ReadString_Raw()
        {
            List<byte> raw = new List<byte>();
            while (true)
            {
                byte cur = Reader.ReadByte();
                if (cur == 0) break;
                else raw.Add(cur);
            }

            var user_encoding = Datamodel.TextEncoding.GetString(raw.ToArray());
            if (user_encoding.Contains('�'))
                return Encoding.Default.GetString(raw.ToArray());
            else return user_encoding;
        }

        class StringDictionary
        {
            Binary Codec;
            BinaryWriter Writer;
            int EncodingVersion;

            List<string> Strings = new List<string>();
            public bool Dummy;

            // binary 4 uses int for dictionary length, but short for dictionary indices. Whoops!
            public byte LengthSize { get { return (byte)(EncodingVersion < 4 ? sizeof(short) : sizeof(int)); } }
            public byte IndiceSize { get { return (byte)(EncodingVersion < 5 ? sizeof(short) : sizeof(int)); } }

            /// <summary>
            /// Constructs a new <see cref="StringDictionary"/> from a Binary stream.
            /// </summary>
            public StringDictionary(Binary codec, BinaryReader reader)
            {
                Codec = codec;
                EncodingVersion = codec.EncodingVersion;
                Dummy = EncodingVersion == 1;
                if (!Dummy)
                {
                    foreach (var i in Enumerable.Range(0, LengthSize == sizeof(short) ? Codec.Reader.ReadInt16() : Codec.Reader.ReadInt32()))
                        Strings.Add(Codec.ReadString_Raw());
                }
            }

            /// <summary>
            /// Constructs a new <see cref="StringDictionary"/> from a <see cref="Datamodel"/> object.
            /// </summary>
            public StringDictionary(int encoding_version, BinaryWriter writer, Datamodel dm)
            {
                EncodingVersion = encoding_version;
                Writer = writer;

                Dummy = EncodingVersion == 1;
                if (!Dummy)
                {
                    Scraped = new HashSet<Element>();

                    ScrapeElement(dm.Root);
                    Strings = Strings.Distinct().ToList();

                    Scraped = null;
                }
            }

            private HashSet<Element> Scraped;

            void ScrapeElement(Element elem)
            {
                if (elem == null || elem.Stub || Scraped.Contains(elem)) return;
                Scraped.Add(elem);

                Strings.Add(elem.Name);
                Strings.Add(elem.ClassName);
                foreach (var attr in elem)
                {
                    Strings.Add(attr.Key);
                    if (attr.Value is string) Strings.Add((string)attr.Value);
                    else if (attr.Value is Element) ScrapeElement((Element)attr.Value);
                    else if (attr.Value is IList<Element>)
                        foreach (var array_elem in (IList<Element>)attr.Value)
                            ScrapeElement(array_elem);
                }
            }

            public string ReadString()
            {
                if (Dummy) return Codec.ReadString_Raw();
                return Strings[IndiceSize == sizeof(short) ? Codec.Reader.ReadInt16() : Codec.Reader.ReadInt32()];
            }

            public void WriteString(string value)
            {
                if (Dummy)
                    Writer.Write(value);
                else
                {
                    var index = Strings.IndexOf(value);
                    if (IndiceSize == sizeof(short)) Writer.Write((short)index);
                    else Writer.Write(index);
                }
            }

            public void WriteSelf()
            {
                if (Dummy) return;

                if (LengthSize == sizeof(short))
                    Writer.Write((short)Strings.Count);
                else
                    Writer.Write(Strings.Count);

                foreach (var str in Strings)
                    Writer.Write(str);
            }
        }
        StringDictionary StringDict;

        public void Encode(Datamodel dm, int encoding_version, Stream stream)
        {
            using (var writer = new DmxBinaryWriter(stream))
                new Encoder(this, writer, dm, encoding_version).Encode();
        }

        float[] ReadVector(int dim)
        {
            var output = new float[dim];
            foreach (int i in Enumerable.Range(0, dim))
                output[i] = Reader.ReadSingle();
            return output;
        }

        object ReadValue(Datamodel dm, Type type, bool raw_string)
        {
            if (type == typeof(Element))
            {
                var index = Reader.ReadInt32();
                if (index == -1)
                    return null;
                else if (index == -2)
                {
                    var id = new Guid(ReadString_Raw()); // yes, it's in ASCII!
                    return dm.AllElements[id] ?? new Element(dm, id);
                }
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

            if (type == typeof(Vector2))
            {
                var ords = ReadVector(2);
                return new Vector2(ords[0], ords[1]);
            }
            if (type == typeof(Vector3))
            {
                var ords = ReadVector(3);
                return new Vector3(ords[0], ords[1], ords[2]);
            }
            if (type == typeof(Vector4))
            {
                var ords = ReadVector(4);
                return new Vector4(ords[0], ords[1], ords[2], ords[3]);
            }
            if (type == typeof(Quaternion))
            {
                var ords = ReadVector(4);
                return new Quaternion(ords[0], ords[1], ords[2], ords[3]);
            }
            if (type == typeof(Matrix4x4))
            {
                var ords = ReadVector(4*4);
                return new Matrix4x4(
                    ords[0], ords[1], ords[2], ords[3],
                    ords[4], ords[5], ords[6], ords[7],
                    ords[8], ords[9], ords[10], ords[11],
                    ords[12], ords[13], ords[14], ords[15]);
            }

            if (type == typeof(byte))
                return Reader.ReadByte();
            if (type == typeof(UInt64))
                return Reader.ReadUInt64();

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
            Reader = new BinaryReader(stream, Datamodel.TextEncoding);

            if (EncodingVersion >= 9)
            {
                // Read prefix elements
                foreach (int prefix_elem in Enumerable.Range(0, Reader.ReadInt32()))
                {
                    foreach (int attr_index in Enumerable.Range(0, Reader.ReadInt32()))
                    {
                        var name = ReadString_Raw();
                        var value = DecodeAttribute(dm);
                        if (prefix_elem == 0) // skip subsequent elements...are they considered "old versions"?
                            dm.PrefixAttributes[name] = value;
                    }
                }
            }

            StringDict = new StringDictionary(this, Reader);
            var num_elements = Reader.ReadInt32();

            // read index
            foreach (var i in Enumerable.Range(0, num_elements))
            {
                var type = StringDict.ReadString();
                var name = EncodingVersion >= 4 ? StringDict.ReadString() : ReadString_Raw();
                var id_bits = Reader.ReadBytes(16);
                var id = new Guid(BitConverter.IsLittleEndian ? id_bits : id_bits.Reverse().ToArray());

                var elem = new Element(dm, name, id, type);
            }

            // read attributes (or not, if we're deferred)
            foreach (var elem in dm.AllElements.ToArray())
            {
                System.Diagnostics.Debug.Assert(!elem.Stub);

                var num_attrs = Reader.ReadInt32();

                foreach (var i in Enumerable.Range(0, num_attrs))
                {
                    var name = StringDict.ReadString();

                    if (defer_mode == DeferredMode.Automatic)
                    {
                        CodecUtilities.AddDeferredAttribute(elem, name, Reader.BaseStream.Position);
                        SkipAttribte();
                    }
                    else
                    {
                        elem.Add(name, DecodeAttribute(dm));
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
            var type = IdToType(Reader.ReadByte());

            if (!Datamodel.IsDatamodelArrayType(type))
                return ReadValue(dm, type, EncodingVersion < 4);
            else
            {
                var count = Reader.ReadInt32();
                var inner_type = Datamodel.GetArrayInnerType(type);
                var array = CodecUtilities.MakeList(inner_type, count);

                foreach (var x in Enumerable.Range(0, count))
                    array.Add(ReadValue(dm, inner_type, true));

                return array;
            }
        }

        void SkipAttribte()
        {
            var type = IdToType(Reader.ReadByte());

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
                if (!StringDict.Dummy && !array && EncodingVersion >= 4)
                    length = StringDict.IndiceSize;
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
            else if (type == typeof(Vector3))
                length = sizeof(float) * 3;
            else if (type == typeof(Vector4) || type == typeof(Quaternion))
                length = sizeof(float) * 4;
            else if (type == typeof(Matrix4x4))
                length = sizeof(float) * 4 * 4;
            else
                length = System.Runtime.InteropServices.Marshal.SizeOf(type);

            Reader.BaseStream.Seek(length * count, SeekOrigin.Current);
        }

        struct Encoder
        {
            Dictionary<Element, int> ElementIndices;
            List<Element> ElementOrder;
            BinaryWriter Writer;
            StringDictionary StringDict;
            Datamodel Datamodel;

            int EncodingVersion;

            public Encoder(Binary codec, BinaryWriter writer, Datamodel dm, int version)
            {
                EncodingVersion = version;
                Writer = writer;
                Datamodel = dm;

                StringDict = new StringDictionary(version, writer, dm);
                ElementIndices = new Dictionary<Element, int>();
                ElementOrder = new List<Element>();

            }

            public void Encode()
            {
                Writer.Write(String.Format(CodecUtilities.HeaderPattern, "binary", EncodingVersion, Datamodel.Format, Datamodel.FormatVersion) + "\n");

                if (EncodingVersion >= 9)
                    Writer.Write((int)0); // Prefix elements

                StringDict.WriteSelf();

                Writer.Write(CountChildren(Datamodel.Root, new HashSet<Element>()));

                WriteIndex(Datamodel.Root);
                foreach (var e in ElementOrder)
                    WriteBody(e);
            }

            int CountChildren(Element elem, HashSet<Element> counted)
            {
                if (elem.Stub) return 0;
                int num_elems = 1;
                counted.Add(elem);
                foreach (var attr in elem)
                {
                    if (attr.Value == null) continue;

                    var child_elem = attr.Value as Element;
                    if (child_elem != null && !counted.Contains(child_elem))
                        num_elems += CountChildren(child_elem, counted);
                    else
                    {
                        var child_array = attr.Value as IEnumerable<Element>;
                        if (child_array != null)
                            foreach (var array_elem in child_array.Where(c => c != null && !counted.Contains(c)))
                                num_elems += CountChildren(array_elem, counted);
                    }
                }
                return num_elems;
            }

            void WriteIndex(Element elem)
            {
                if (elem.Stub) return;

                ElementIndices[elem] = ElementIndices.Count;
                ElementOrder.Add(elem);

                StringDict.WriteString(elem.ClassName);
                if (EncodingVersion >= 4) StringDict.WriteString(elem.Name);
                else Writer.Write(elem.Name);
                Writer.Write(elem.ID.ToByteArray());

                foreach (var attr in elem)
                {
                    var child_elem = attr.Value as Element;
                    if (child_elem != null)
                    {
                        if (!ElementIndices.ContainsKey(child_elem))
                            WriteIndex(child_elem);
                    }
                    else
                    {
                        var elem_list = attr.Value as IList<Element>;
                        if (elem_list != null)
                        {
                            var elem_indices = ElementIndices; // workaround for .Net 4 lambda limitation in structs
                            foreach (var item in elem_list.Where(e => e != null && !elem_indices.ContainsKey(e)))
                                WriteIndex(item);
                        }
                    }
                }
            }

            void WriteBody(Element elem)
            {
                Writer.Write(elem.Count);
                foreach (var attr in elem)
                {
                    StringDict.WriteString(attr.Key);
                    var attr_type = attr.Value == null ? typeof(Element) : attr.Value.GetType();
                    Writer.Write(TypeToId(attr_type, EncodingVersion));

                    if (attr.Value == null || !Datamodel.IsDatamodelArrayType(attr.Value.GetType()))
                        WriteAttribute(attr.Value, false);
                    else
                    {
                        var array = (System.Collections.IList)attr.Value;
                        Writer.Write(array.Count);
                        attr_type = Datamodel.GetArrayInnerType(array.GetType());
                        foreach (var item in array)
                            WriteAttribute(item, true);
                    }
                }
            }

            void WriteAttribute(object value, bool in_array)
            {
                if (value == null)
                {
                    Writer.Write(-1);
                    return;
                }

                if (value is Element)
                {
                    var child_elem = (Element)value;
                    if (child_elem.Stub)
                    {
                        Writer.Write(-2);
                        Writer.Write(child_elem.ID.ToString().ToArray()); // yes, ToString()!
                        Writer.Write((byte)0);
                    }
                    else
                        Writer.Write(ElementIndices[child_elem]);
                    return;
                }

                if (value is string)
                {
                    if (EncodingVersion < 4 || in_array)
                        Writer.Write((string)value);
                    else
                        StringDict.WriteString((string)value);
                    return;
                }

                if (value is bool)
                {
                    Writer.Write((bool)value == true ? (byte)1 : (byte)0);
                    return;
                }

                if (value is byte[])
                {
                    var binary_value = (byte[])value;
                    Writer.Write(binary_value.Length);
                    Writer.Write(binary_value);
                    return;
                }

                if (value is TimeSpan)
                {
                    Writer.Write((int)(((TimeSpan)value).TotalSeconds * 10000));
                    return;
                }

                if (value is System.Drawing.Color)
                {
                    var colour_value = (System.Drawing.Color)value;
                    Writer.Write(new byte[] { colour_value.R, colour_value.G, colour_value.B, colour_value.A });
                    return;
                }

                if (value is Vector2)
                {
                    var v = (Vector2)value;
                    Writer.Write(v.X);
                    Writer.Write(v.Y);
                    return;
                }
                if (value is Vector3)
                {
                    var v = (Vector3)value;
                    Writer.Write(v.X);
                    Writer.Write(v.Y);
                    Writer.Write(v.Z);
                    return;
                }
                if (value is Vector4)
                {
                    var v = (Vector4)value;
                    Writer.Write(v.X);
                    Writer.Write(v.Y);
                    Writer.Write(v.Z);
                    Writer.Write(v.W);
                    return;
                }
                if (value is Quaternion)
                {
                    var v = (Quaternion)value;
                    Writer.Write(v.X);
                    Writer.Write(v.Y);
                    Writer.Write(v.Z);
                    Writer.Write(v.W);
                    return;
                }
                if (value is Matrix4x4)
                {
                    var v = (Matrix4x4)value;
                    Writer.Write(v.M11);
                    Writer.Write(v.M12);
                    Writer.Write(v.M13);
                    Writer.Write(v.M14);
                    Writer.Write(v.M21);
                    Writer.Write(v.M22);
                    Writer.Write(v.M23);
                    Writer.Write(v.M24);
                    Writer.Write(v.M31);
                    Writer.Write(v.M32);
                    Writer.Write(v.M33);
                    Writer.Write(v.M34);
                    Writer.Write(v.M41);
                    Writer.Write(v.M42);
                    Writer.Write(v.M43);
                    Writer.Write(v.M44);
                    return;
                }

                if (value is int)
                {
                    Writer.Write((int)value);
                    return;
                }
                if (value is float)
                {
                    Writer.Write((float)value);
                    return;
                }
                if (value is byte)
                {
                    Writer.Write((byte)value);
                    return;
                }
                if (value is UInt64)
                {
                    Writer.Write((UInt64)value);
                    return;
                }

                throw new InvalidOperationException("Unrecognised output Type.");
            }
        }

        class DmxBinaryWriter : BinaryWriter
        {
            public DmxBinaryWriter(Stream output)
                : base(output, Datamodel.TextEncoding)
            { }

            /// <summary>
            /// Writes a null-terminated string to the underlying stream using <see cref="Datamodel.TextEncoding"/>.
            /// </summary>
            /// <param name="value"></param>
            [System.Security.SecuritySafeCritical]
            public override void Write(string value)
            {
                if (value != null)
                    base.Write(Datamodel.TextEncoding.GetBytes(value));
                base.Write((byte)0);
            }

            protected override void Dispose(bool disposing)
            {
                return; // don't mess with the base stream!
            }
        }
    }
}
