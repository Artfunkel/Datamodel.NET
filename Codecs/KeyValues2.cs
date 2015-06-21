using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Datamodel.Codecs
{
    [CodecFormat("keyvalues2", 1)]
    [CodecFormat("keyvalues2", 2)]
    [CodecFormat("keyvalues2", 3)]
    [CodecFormat("keyvalues2", 4)]
    class KeyValues2 : ICodec, IDisposable
    {
        TextReader Reader;
        KV2Writer Writer;
        Datamodel DM;

        static readonly Dictionary<Type, string> TypeNames = new Dictionary<Type, string>();
        static readonly Dictionary<int, Type[]> ValidAttributes = new Dictionary<int, Type[]>();
        static KeyValues2()
        {
            TypeNames[typeof(Element)] = "element";
            TypeNames[typeof(int)] = "int";
            TypeNames[typeof(float)] = "float";
            TypeNames[typeof(bool)] = "bool";
            TypeNames[typeof(string)] = "string";
            TypeNames[typeof(byte[])] = "binary";
            TypeNames[typeof(TimeSpan)] = "time";
            TypeNames[typeof(System.Drawing.Color)] = "color";
            TypeNames[typeof(Vector2)] = "vector2";
            TypeNames[typeof(Vector3)] = "vector3";
            TypeNames[typeof(Vector4)] = "vector4";
            TypeNames[typeof(Angle)] = "qangle";
            TypeNames[typeof(Quaternion)] = "quaternion";
            TypeNames[typeof(Matrix)] = "matrix";

            ValidAttributes[1] = ValidAttributes[2] = ValidAttributes[3] = TypeNames.Select(kv => kv.Key).ToArray();

            TypeNames[typeof(byte)] = "uint8";
            TypeNames[typeof(UInt64)] = "uint64";

            ValidAttributes[4] = TypeNames.Select(kv => kv.Key).ToArray();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "DM")]
        public void Dispose()
        {
            if (Reader != null) Reader.Dispose();
            if (Writer != null) Writer.Dispose();
        }

        #region Encode
        class KV2Writer : IDisposable
        {
            public int Indent
            {
                get { return indent_count; }
                set
                {
                    indent_count = value;
                    indent_string = "\n" + String.Concat(Enumerable.Repeat("\t", value));
                }
            }
            int indent_count = 0;
            string indent_string = "\n";
            TextWriter Output;

            public KV2Writer(Stream output)
            {
                Output = new StreamWriter(output, Datamodel.TextEncoding);
            }

            public void Dispose()
            {
                Output.Dispose();
            }

            string Sanitise(string value)
            {
                return value == null ? null : value.Replace("\"", "\\\"");
            }

            /// <summary>
            /// Writes the string straight to the output steam, with no sanitisation.
            /// </summary>
            public void Write(string value)
            {
                Output.Write(value);
            }

            public void WriteTokens(params string[] values)
            {
                Output.Write("\"{0}\"", String.Join("\" \"", values.Select(s => Sanitise(s))));
            }

            public void WriteLine()
            {
                Output.Write(indent_string);
            }

            /// <summary>
            /// Writes a new line followed by the given value
            /// </summary>
            public void WriteLine(string value)
            {
                WriteLine();
                Output.Write(value);
            }

            public void WriteTokenLine(params string[] values)
            {
                Output.Write(indent_string);
                WriteTokens(values);
            }

            public void TrimEnd(int count)
            {
                if (count > 0)
                {
                    Output.Flush();
                    var stream = ((StreamWriter)Output).BaseStream;
                    stream.SetLength(stream.Length - count);
                }
            }

            public void Flush()
            {
                Output.Flush();
            }
        }
        int EncodingVersion;

        Dictionary<Element, int> Users;

        void CountUsers(Element elem)
        {
            if (Users.ContainsKey(elem))
                Users[elem]++;
            else
            {
                Users[elem] = 1;
                foreach (var attr in elem)
                {
                    if (attr.Value == null) continue;
                    var child_elem = attr.Value as Element;
                    if (child_elem != null)
                        CountUsers(child_elem);
                    else
                    {
                        var enumerable = attr.Value as IEnumerable<Element>;
                        if (enumerable != null)
                            foreach (var array_elem in enumerable.Where(c => c != null))
                                CountUsers(array_elem);
                    }
                }
            }
        }

        void WriteAttribute(string name, Type type, object value, bool in_array)
        {
            bool is_element = type == typeof(Element);

            Type inner_type = null;
            if (!in_array)
            {
                inner_type = Datamodel.GetArrayInnerType(type);
                if (inner_type == typeof(byte) && !ValidAttributes[EncodingVersion].Contains(typeof(byte)))
                    inner_type = null; // fall back on the "binary" type in older KV2 versions
            }
            if (!ValidAttributes[EncodingVersion].Contains(inner_type ?? type))
                throw new CodecException(type.Name + " is not valid in KeyValues2 " + EncodingVersion);

            if (inner_type != null)
            {
                is_element = inner_type == typeof(Element);

                Writer.WriteTokenLine(name, TypeNames[inner_type] + "_array");

                if (((System.Collections.IList)value).Count == 0)
                {
                    Writer.Write(" [ ]");
                    return;
                }

                if (is_element) Writer.WriteLine("[");
                else Writer.Write(" [");

                Writer.Indent++;
                foreach (var array_value in (System.Collections.IList)value)
                    WriteAttribute(null, inner_type, array_value, true);
                Writer.Indent--;
                Writer.TrimEnd(1); // remove trailing comma

                if (inner_type == typeof(Element)) Writer.WriteLine("]");
                else Writer.Write(" ]");
                return;
            }

            if (is_element)
            {
                var elem = value as Element;
                var id = elem == null ? "" : elem.ID.ToString();

                if (in_array)
                {
                    if (elem != null && Users[elem] == 1)
                    {
                        Writer.WriteLine();
                        WriteElement(elem);
                    }
                    else
                        Writer.WriteTokenLine("element", id);
                    Writer.Write(",");
                }
                else
                {
                    if (elem != null && Users.ContainsKey(elem) && Users[elem] == 1)
                    {
                        Writer.WriteLine(String.Format("\"{0}\" ", name));
                        WriteElement(elem);
                    }
                    else
                        Writer.WriteTokenLine(name, "element", id);
                }
            }
            else
            {
                if (type == typeof(bool))
                    value = (bool)value ? 1 : 0;
                else if (type == typeof(float))
                    value = (float)value;
                else if (type == typeof(byte[]))
                    value = BitConverter.ToString((byte[])value).Replace("-", String.Empty);
                else if (type == typeof(TimeSpan))
                    value = ((TimeSpan)value).TotalSeconds;
                else if (type == typeof(System.Drawing.Color))
                {
                    var c = (System.Drawing.Color)value;
                    value = String.Join(" ", new int[] { c.R, c.G, c.B, c.A });
                }
                else if (type == typeof(UInt64))
                    value = ((UInt64)value).ToString("X");

                if (in_array)
                    Writer.Write(String.Format(" \"{0}\",", value.ToString()));
                else
                    Writer.WriteTokenLine(name, TypeNames[type], value.ToString());
            }

        }

        void WriteElement(Element element)
        {
            if (TypeNames.ContainsValue(element.ClassName))
                throw new CodecException(String.Format("Element {} uses reserved type name \"{1}\".", element.ID, element.ClassName));
            Writer.WriteTokens(element.ClassName);
            Writer.WriteLine("{");
            Writer.Indent++;
            Writer.WriteTokenLine("id", "elementid", element.ID.ToString());
            Writer.WriteTokenLine("name", "string", element.Name);

            foreach (var attr in element)
            {
                if (attr.Value == null)
                    WriteAttribute(attr.Key, typeof(Element), null, false);
                else
                    WriteAttribute(attr.Key, attr.Value.GetType(), attr.Value, false);
            }

            Writer.Indent--;
            Writer.WriteLine("}");
        }

        public void Encode(Datamodel dm, int encoding_version, Stream stream)
        {
            Writer = new KV2Writer(stream);
            EncodingVersion = encoding_version;

            Writer.Write(String.Format(CodecUtilities.HeaderPattern, "keyvalues2", encoding_version, dm.Format, dm.FormatVersion));
            Writer.WriteLine();

            Users = new Dictionary<Element, int>();

            if (EncodingVersion >= 9 && dm.PrefixAttributes.Count > 0)
            {
                Writer.WriteTokens("$prefix_element$");
                Writer.WriteLine("{");
                Writer.Indent++;
                foreach (var attr in dm.PrefixAttributes)
                    WriteAttribute(attr.Key, attr.Value.GetType(), attr.Value, false);
                Writer.Indent--;
                Writer.WriteLine("}");
            }

            CountUsers(dm.Root);
            WriteElement(dm.Root);
            Writer.WriteLine();

            foreach (var pair in Users.Where(pair => pair.Value > 1))
            {
                if (pair.Key == dm.Root)
                    continue;
                Writer.WriteLine();
                WriteElement(pair.Key);
                Writer.WriteLine();
            }

            Writer.Flush();
        }
        #endregion

        #region Decode
        StringBuilder TokenBuilder = new StringBuilder();
        int Line = 0;
        string Decode_NextToken()
        {
            TokenBuilder.Clear();
            bool escaped = false;
            bool in_block = false;
            while (true)
            {
                var read = Reader.Read();
                if (read == -1) throw new EndOfStreamException();
                var c = (Char)read;
                if (escaped)
                {
                    TokenBuilder.Append(c);
                    escaped = false;
                    continue;
                }
                switch (c)
                {
                    case '"':
                        if (in_block) return TokenBuilder.ToString();
                        in_block = true;
                        break;
                    case '\\':
                        escaped = true; break;
                    case '\r':
                    case '\n':
                        Line++;
                        break;
                    case '{':
                    case '}':
                    case '[':
                    case ']':
                        if (!in_block)
                            return c.ToString();
                        else goto default;
                    default:
                        if (in_block) TokenBuilder.Append(c);
                        break;
                }
            }
        }

        Element Decode_ParseElementId()
        {
            Element elem;
            var id_s = Decode_NextToken();

            if (String.IsNullOrEmpty(id_s))
                elem = null;
            else
            {
                Guid id = new Guid(id_s);
                elem = DM.AllElements[id];
                if (elem == null)
                    elem = new Element(DM, id);
            }
            return elem;
        }

        Element Decode_ParseElement(string class_name)
        {
            string elem_class = class_name ?? Decode_NextToken();
            string elem_name = null;
            string elem_id = null;
            Element elem = null;

            string next = Decode_NextToken();
            if (next != "{") throw new CodecException(String.Format("Expected Element opener, got '{0}'.", next));
            while (true)
            {
                next = Decode_NextToken();
                if (next == "}") break;

                var attr_name = next;
                var attr_type_s = Decode_NextToken();
                var attr_type = TypeNames.FirstOrDefault(kv => kv.Value == attr_type_s.Split('_')[0]).Key;

                if (elem == null && attr_name == "id" && attr_type_s == "elementid")
                {
                    elem_id = Decode_NextToken();
                    var id = new Guid(elem_id);
                    var local_element = DM.AllElements[id];
                    if (local_element != null)
                    {
                        elem = local_element;
                        elem.Name = elem_name;
                        elem.ClassName = elem_class;
                        elem.Stub = false;
                    }
                    else if (elem_class != "$prefix_element$")
                        elem = new Element(DM, elem_name, new Guid(elem_id), elem_class);

                    continue;
                }

                if (attr_name == "name" && attr_type == typeof(string))
                {
                    elem_name = Decode_NextToken();
                    if (elem != null)
                        elem.Name = elem_name;
                    continue;
                }

                if (elem == null)
                    continue;

                if (attr_type_s == "element")
                {
                    elem.Add(attr_name, Decode_ParseElementId());
                    continue;
                }

                object attr_value = null;

                if (attr_type == null)
                    attr_value = Decode_ParseElement(attr_type_s);
                else if (attr_type_s.EndsWith("_array"))
                {
                    var array = CodecUtilities.MakeList(attr_type, 5); // assume 5 items
                    attr_value = array;

                    next = Decode_NextToken();
                    if (next != "[") throw new CodecException(String.Format("Expected array opener, got '{0}'.", next));
                    while (true)
                    {
                        next = Decode_NextToken();
                        if (next == "]") break;

                        if (next == "element") // Element ID reference
                            array.Add(Decode_ParseElementId());
                        else if (attr_type == typeof(Element)) // inline Element
                            array.Add(Decode_ParseElement(next));
                        else // normal value
                            array.Add(Decode_ParseValue(attr_type, next));
                    }
                }
                else
                    attr_value = Decode_ParseValue(attr_type, Decode_NextToken());

                if (elem != null)
                    elem.Add(attr_name, attr_value);
                else
                    DM.PrefixAttributes[attr_name] = attr_value;
            }
            return elem;
        }

        object Decode_ParseValue(Type type, string value)
        {
            if (type == typeof(string))
                return value;

            value = value.Trim();

            if (type == typeof(Element))
                return Decode_ParseElement(value);
            if (type == typeof(int))
                return Int32.Parse(value);
            else if (type == typeof(float))
                return float.Parse(value);
            else if (type == typeof(bool))
                return byte.Parse(value) == 1;
            else if (type == typeof(byte[]))
            {
                byte[] result = new byte[value.Length / 2];
                for (int i = 0; i * 2 < value.Length; i++)
                {
                    result[i] = byte.Parse(value.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
                }
                return result;
            }
            else if (type == typeof(TimeSpan))
                return TimeSpan.FromSeconds(float.Parse(value));

            var num_list = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            if (type == typeof(System.Drawing.Color))
            {
                var rgba = num_list.Select(i => byte.Parse(i)).ToArray();
                return System.Drawing.Color.FromArgb(rgba[3], rgba[0], rgba[1], rgba[2]);
            }

            var f_list = num_list.Select(i => float.Parse(i));
            if (type == typeof(Vector2)) return new Vector2(f_list);
            else if (type == typeof(Vector3)) return new Vector3(f_list);
            else if (type == typeof(Vector4)) return new Vector4(f_list);
            else if (type == typeof(Angle)) return new Angle(f_list);
            else if (type == typeof(Quaternion)) return new Quaternion(f_list);
            else if (type == typeof(Matrix)) return new Matrix(f_list);

            if (type == typeof(byte)) return byte.Parse(value);
            if (type == typeof(UInt64)) return UInt64.Parse(value.Remove(0, 2),System.Globalization.NumberStyles.HexNumber);

            else throw new ArgumentException("Internal error: ParseValue passed unsupported Type.");
        }

        public Datamodel Decode(int encoding_version, string format, int format_version, Stream stream, DeferredMode defer_mode)
        {
            DM = new Datamodel(format, format_version);

            stream.Seek(0, SeekOrigin.Begin);
            Reader = new StreamReader(stream, Datamodel.TextEncoding);
            Reader.ReadLine(); // skip DMX header
            Line = 1;
            string next;

            while (true)
            {
                try
                { next = Decode_NextToken(); }
                catch (EndOfStreamException)
                { break; }

                try
                { Decode_ParseElement(next); }
                catch (Exception err)
                { throw new CodecException(String.Format("KeyValues2 decode failed on line {0}:\n\n{1}", Line, err.Message), err); }
            }

            return DM;
        }
        #endregion
    }
}
