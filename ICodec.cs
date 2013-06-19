using System;
using System.IO;

namespace Datamodel.Codecs
{
    /// <summary>
    /// Defines methods for the encoding and decoding of <see cref="Datamodel"/> objects. Codecs are registered with <see cref="Datamodel.RegisterCodec"/>.
    /// </summary>
    /// <remarks>A new ICodec is instantiated for every encode/decode operation.</remarks>
    /// <seealso cref="CodecUtilities"/>
    public interface ICodec
    {
        /// <summary>
        /// Encodes a <see cref="Datamodel"/> to a <see cref="Stream"/>.
        /// </summary>
        /// <param name="dm">The Datamodel to encode.</param>
        /// <param name="encoding_version">The encoding version to use.</param>
        /// <param name="stream">The output stream.</param>
        void Encode(Datamodel dm, int encoding_version, Stream stream);

        /// <summary>
        /// Decodes a <see cref="Datamodel"/> from a <see cref="Stream"/>.
        /// </summary>
        /// <param name="encoding_version">The encoding version that this stream uses.</param>
        /// <param name="format">The format of the Datamodel.</param>
        /// <param name="format_version">The format version of the Datamodel.</param>
        /// <param name="stream">The input stream. Its position will always be 0. Do not dispose.</param>
        /// <param name="defer_mode">The deferred loading mode specified by the caller. Only relevant to implementers of <see cref="IDeferredAttributeCodec"/></param>
        /// <returns></returns>
        Datamodel Decode(int encoding_version, string format, int format_version, Stream stream, DeferredMode defer_mode);
    }

    /// <summary>
    /// Defines methods for the deferred loading of <see cref="Attribute"/> values.
    /// </summary>
    /// <remarks>
    /// <para>Implementers must still load all elements and Attribute names. Only Attribute values can be streamed.</para>
    /// <para>IDeferredAttributeCodec objects will be attached to their host Datamodel for the duration of its life.</para>
    /// </remarks>
    /// <seealso cref="CodecUtilities"/>
    public interface IDeferredAttributeCodec : ICodec
    {
        /// <summary>
        /// Called when an unloaded <see cref="Attribute"/> is accessed.
        /// </summary>
        /// <param name="dm">The <see cref="Datamodel"/> to which the Attribute belongs.</param>
        /// <param name="offset">The offset at which the Attribute begins in the source <see cref="Stream"/>.</param>
        /// <returns>The Attribute's value.</returns>
        object DeferredDecodeAttribute(Datamodel dm, long offset);
    }

    /// <summary>
    /// Values which instruct <see cref="IDeferredAttributeCodec"/> implementers on how to use deferred Attribute reading.
    /// </summary>
    public enum DeferredMode
    {
        /// <summary>
        /// The codec decides whether to defer attribute loading.
        /// </summary>
        Automatic,
        /// <summary>
        /// The codec loads all attributes immediately.
        /// </summary>
        Disabled
    }

    /// <summary>
    /// Helper methods for <see cref="ICodec"/> implementers.
    /// </summary>
    public static class CodecUtilities
    {
        /// <summary>
        /// Creates a new <see cref="Attribute"/> on an existing <see cref="Element"/>. This method is intended for <see cref="ICodec"/> implementers and should not be directly called from any other code.
        /// </summary>
        /// <param name="elem">The Element to add to.</param>
        /// <param name="name">The name of the Attribute. Must be unique on the Element.</param>
        /// <param name="value">The value held by the Attribute. Must be a valid Datamodel type, or null. Null means either an empty Element reference, or if the offset argument is not 0 that the value has not been loaded yet.</param>
        /// <param name="offset">If using deferred loading, the location within the source stream at which this Attribute's value is located. Otherwise 0.</param>
        public static void AddAttribute(Element elem, string name, object value, long offset)
        {
            new Attribute(elem, name, value, offset);
        }

        /// <summary>
        /// Standard DMX header with CLR-style variable tokens.
        /// </summary>
        public const string HeaderPattern = "<!-- dmx encoding {0} {1} format {2} {3} -->";
        /// <summary>
        /// Standard DMX header as a regular expression pattern.
        /// </summary>
        public const string HeaderPattern_Regex = "<!-- dmx encoding (\\S+) ([0-9]+) format (\\S+) ([0-9]+) -->";
        //public const string HeaderPattern_Proto2 = "<!-- DMXVersion binary_v{0} -->";
    }
}
