using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace Datamodel.Codecs
{
    class KeyValues2 : ICodec, IDisposable
    {
        TextReader Reader;
        KV2Writer Writer;

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
            TypeNames[typeof(Angle)] = "angle";
            TypeNames[typeof(Quaternion)] = "quaternion";
            TypeNames[typeof(Matrix)] = "matrix";

            ValidAttributes[1] = TypeNames.Select(kv => kv.Key).ToArray();
        }

        public void Dispose()
        {
            if (Reader != null) Reader.Dispose();
            if (Writer != null) Writer.Dispose();
        }

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
                Output = new StreamWriter(output, Encoding.ASCII);
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
                    var stream = (Output as StreamWriter).BaseStream;
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
                    if (child.GetType() == typeof(Element)) CountUsers(child as Element);
                    if (child.GetType() == typeof(List<Element>))
                        foreach (var child_elem in (child as List<Element>).Where(c => c != null))
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

                        if ((value as System.Collections.ICollection).Count == 0)
                        {
                            Writer.Write(" [ ]");
                            return;
                        }

                        if (is_element) Writer.WriteLine("[");
                        else Writer.Write(" [");

                        Writer.Indent++;
                        foreach (var array_value in value as System.Collections.ICollection)
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
                            value = BitConverter.ToString(value as byte[]).Replace("-", String.Empty);

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

        static Regex TokenRegex = new Regex("(\".*?\"|{|}|\\[|\\])");

        string[] ReadTokens()
        {
            var raw = Reader.ReadLine();
            if (raw == null) return null;
            else return ParseTokens(raw.Trim());
        }
        string[] ParseTokens(string raw)
        {
            var matches = new List<string>();
            foreach (Match match in TokenRegex.Matches(raw))
                matches.Add(match.Value.Trim('"'));
            return matches.ToArray();
        }

        public Datamodel Decode(int encoding_version, string format, int format_version, Stream stream, DeferredMode defer_mode)
        {
            Datamodel dm = new Datamodel(format, format_version);

            stream.Seek(0, SeekOrigin.Begin);
            Reader = new StreamReader(stream);
            Reader.ReadLine();

            string[] line = null;

            Func<Type, string, object> ParseValue = null;
            Func<string, Element> ParseElem = null;

            ParseValue = (type, value) =>
                {
                    if (type == typeof(Element))
                        return ParseElem(value);
                    if (type == typeof(int))
                        return Int32.Parse(value);
                    else if (type == typeof(float))
                        return float.Parse(value);
                    else if (type == typeof(bool))
                        return byte.Parse(value) == 1;
                    else if (type == typeof(string))
                        return value;
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
                    else if (type == typeof(System.Drawing.Color))
                    {
                        var rgba = value.Split(' ').Select(i => byte.Parse(i)).ToArray();
                        return System.Drawing.Color.FromArgb(rgba[3], rgba[0], rgba[1], rgba[2]);
                    }

                    else if (type == typeof(Vector2))
                        return new Vector2(value.Split(' ').Select(i => float.Parse(i)));
                    else if (type == typeof(Vector3))
                        return new Vector3(value.Split(' ').Select(i => float.Parse(i)));
                    else if (type == typeof(Vector4))
                        return new Vector4(value.Split(' ').Select(i => float.Parse(i)));
                    else if (type == typeof(Angle))
                        return new Angle(value.Split(' ').Select(i => float.Parse(i)));
                    else if (type == typeof(Quaternion))
                        return new Quaternion(value.Split(' ').Select(i => float.Parse(i)));
                    else if (type == typeof(Matrix))
                        return new Matrix(value.Split(' ').Select(i => float.Parse(i)));

                    else throw new ArgumentException("Unsupported Type.");
                };

            ParseElem = (type) =>
                {
                    Reader.ReadLine(); // {

                    Element elem = null;
                    string elem_id = null;
                    string elem_name = null;
                    while (true)
                    {
                        line = ReadTokens();
                        if (line.Length == 0) continue;
                        if (line[0] == "}") break;

                        if (elem == null)
                        {
                            if (line[0] == "id") elem_id = line[2];
                            if (line[0] == "name") elem_name = line[2];

                            if (elem_id != null && elem_name != null)
                                elem = dm.CreateElement(elem_name, new Guid(elem_id), type);
                            continue;
                        }

                        string attr_name = line[0];

                        if (line[1] == "element")
                        {
                            Element elem_from_id;
                            if (String.IsNullOrEmpty(line[2]))
                                elem_from_id = null;
                            else
                            {
                                Guid id = new Guid(line[2]);
                                elem_from_id = dm.AllElements[id];
                                if (elem_from_id == null)
                                    elem_from_id = dm.CreateStubElement(id);
                            }
                            new Attribute(elem, attr_name, elem_from_id, 0);
                            continue;
                        }

                        bool array = line[1].EndsWith("_array");
                        var attr_type = TypeNames.FirstOrDefault(kv => kv.Value == line[1].Split('_')[0]).Key;

                        if (attr_type == null) // it was an Element type, not a data type
                        {
                            new Attribute(elem, attr_name, ParseElem(line[1]), 0);
                            continue;
                        }

                        Type inner_type = null;
                        object attr_value;
                        if (array)
                        {
                            inner_type = attr_type;
                            attr_type = attr_type.MakeListType();
                        }

                        if (array)
                        {
                            if (inner_type == typeof(Element))
                            {
                                var elem_list = new List<Element>();
                                attr_value = elem_list;
                                if (!line.Contains("]")) // Element lists are either multi-line or empty
                                    while (true)
                                    {
                                        line = ReadTokens();
                                        if (line.Length == 0 || line[0] == "[") continue;
                                        if (line[0] == "]") break;

                                        if (line[0] == "element") elem_list.Add(dm.CreateStubElement(new Guid(line[1])));
                                        else elem_list.Add(ParseElem(line[0]));
                                    }
                            }
                            else
                            {
                                IEnumerable<string> values = null;
                                if (line.Length > 3 && line[2] == "[")
                                    values = line.Skip(3);
                                else
                                {
                                    StringBuilder whole_array = new StringBuilder();
                                    char last = '\0';
                                    bool escaped = false;
                                    while (true)
                                    {
                                        escaped = !escaped && last == '\\';

                                        last = (char)Reader.Read();
                                        if (!escaped && last == ']') break;

                                        whole_array.Append(last);
                                    }
                                    values = ParseTokens(whole_array.ToString());
                                }

                                attr_value = attr_type.GetConstructor(Type.EmptyTypes).Invoke(null);
                                foreach (var value in values.Where(s => s != "]" && s != "["))
                                    (attr_value as System.Collections.IList).Add(ParseValue(inner_type, value));
                            }
                        }
                        else if (line[1] == "binary")
                        {
                            Reader.ReadLine(); // skip opening quote
                            var hex = Reader.ReadLine().Trim();
                            attr_value = hex == "\"" ? new byte[0] : ParseValue(attr_type, hex);
                        }
                        else
                        {
                            attr_value = ParseValue(attr_type, line[2]);
                        }
                        new Attribute(elem, attr_name, attr_value, 0);

                    }

                    return elem;
                };

            while (true)
            {
                line = ReadTokens();
                if (line == null) break;
                if (line.Length == 0) continue;
                else ParseElem(line[0]);
            }

            return dm;
        }
    }
}
