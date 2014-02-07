using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Datamodel.Codecs
{
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

            ValidAttributes[1] = TypeNames.Select(kv => kv.Key).ToArray();
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
                Output = new StreamWriter(output, Encoding.UTF8);
            }

            public void Dispose()
            {
                Output.Dispose();
            }

            public void Write(string value)
            {
                Output.Write(value);
            }

            public void WriteTokens(params string[] values)
            {
                Output.Write("\"{0}\"", String.Join("\" \"", values));
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
                foreach (var child in elem.Select(a => a.Value))
                {
                    if (child == null) continue;
                    if (child.GetType() == typeof(Element)) CountUsers((Element)child);
                    if (child.GetType() == typeof(List<Element>))
                        foreach (var child_elem in ((List<Element>)child).Where(c => c != null))
                            CountUsers(child_elem);
                }
            }
        }

        public void Encode(Datamodel dm, int encoding_version, Stream stream)
        {
            Writer = new KV2Writer(stream);
            EncodingVersion = encoding_version;

            Writer.Write(String.Format(CodecUtilities.HeaderPattern, "keyvalues2", encoding_version, dm.Format, dm.FormatVersion));
            Writer.WriteLine();

            Users = new Dictionary<Element, int>();

            Action<Element> write_element = null;
            Action<string, Type, object, bool> write_attribute = null;

            write_attribute = (name, type, value, in_array) =>
                {
                    bool is_element = type == typeof(Element);

                    var inner_type = Datamodel.GetArrayInnerType(type);

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
                            write_attribute(null, inner_type, array_value, true);
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
                                write_element(elem);
                            }
                            else
                                Writer.WriteTokenLine("element", id);
                            Writer.Write(",");
                        }
                        else
                        {
                            if (elem != null && Users[elem] == 1)
                            {
                                Writer.WriteLine(String.Format("\"{0}\" ", name));
                                write_element(elem);
                            }
                            else
                                Writer.WriteTokenLine(name, "element", id);
                        }
                    }
                    else
                    {
                        if (type == typeof(bool))
                            value = (bool)value ? 1 : 0;
                        if (type == typeof(float))
                            value = (double)(float)value;
                        if (type == typeof(byte[]))
                            value = BitConverter.ToString((byte[])value).Replace("-", String.Empty);
                        if (type == typeof(TimeSpan))
                            value = ((TimeSpan)value).TotalSeconds;
                        if (type == typeof(System.Drawing.Color))
                        {
                            var c = (System.Drawing.Color)value;
                            value = String.Join(" ", new int[] { c.R, c.G, c.B, c.A });
                        }

                        if (in_array)
                            Writer.Write(String.Format(" \"{0}\",", value.ToString()));
                        else
                            Writer.WriteTokenLine(name, TypeNames[type], value.ToString());
                    }

                };

            write_element = (element) =>
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
                        write_attribute(attr.Name, typeof(Element), null, false);
                    else
                        write_attribute(attr.Name, attr.Value.GetType(), attr.Value, false);
                }

                Writer.Indent--;
                Writer.WriteLine("}");
            };

            CountUsers(dm.Root);
            write_element(dm.Root);
            Writer.WriteLine();

            foreach (var pair in Users.Where(pair => pair.Value > 1))
            {
                Writer.WriteLine();
                write_element(pair.Key);
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
                    elem = DM.CreateStubElement(id);
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

                if (elem == null)
                {
                    if (attr_name == "name" && attr_type == typeof(string))
                        elem_name = Decode_NextToken();
                    if (attr_name == "id" && attr_type_s == "elementid")
                        elem_id = Decode_NextToken();
                    if (elem_name != null && elem_id != null)
                        elem = DM.CreateElement(elem_name, new Guid(elem_id), elem_class);
                    continue;
                }

                if (attr_type_s == "element")
                {
                    new Attribute(elem, attr_name, Decode_ParseElementId(), 0);
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

                new Attribute(elem, attr_name, attr_value, 0);
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

            else throw new ArgumentException("Internal error: ParseValue passed unsupported Type.");
        }

        public Datamodel Decode(int encoding_version, string format, int format_version, Stream stream, DeferredMode defer_mode)
        {
            DM = new Datamodel(format, format_version);

            stream.Seek(0, SeekOrigin.Begin);
            Reader = new StreamReader(stream);
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
                { throw new CodecException(String.Format("KeyValues2 decode failed on line {0}: {1}", Line, err.Message), err); }
            }

            return DM;
        }
        #endregion
    }
}
