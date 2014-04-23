Datamodel.NET is a CLR library which implements the Datamodel structure and Datamodel Exchange file format.

Datamodel is a strongly-typed generic data structure designed by Valve Corporation for use in their games. Datamodel Exchange is a Datamodel container file format with multiple possible encodings; binary and ASCII ("keyvalues2") are included.

Datamodel Attributes
--------------------

The following CLR types are supported as Datamodel attributes:

* `int`
* `float`
* `bool`
* `string`
* `byte[]`
* `System.TimeSpan`
* `System.Drawing.Color`

Additionally, the following Datamodel.NET types are supported:

* `Element` (a named collection of attributes)
* `Vector2`
* `Vector3` / `Angle`
* `Vector4` / `Quaternion`
* `Matrix` (4x4)

`IList<T>` collections of the above types are also supported.

Datamodel.NET features
----------------------

* Threaded, thread-safe
* Support for all known versions of Valve's `binary` and `keyvalues2` DMX encodings
* Convenient `IEnumerable`, `INotifyPropertyChanged` and `INotifyCollectionChanged` implementations
* Supports partial trust
* Supports XAML
* Inline documentation
* Binary codec supports just-in-time attribute loading
* Write your own codecs with the `ICodec` interface

Quick example
-------------

```c#
var HelloWorld = new Datamodel.Datamodel("helloworld", 1); // must provide a format name (can be anything) and version

HelloWorld.Root = new Datamodel.Element(HelloWorld, "my_root");
HelloWorld.Root["Hello"] = "World"; // any supported attribute type can be assigned

var MyString = HelloWorld.Root.Get<string>("Hello");

HelloWorld.Save("hello world.dmx", "keyvalues2", 1); // must provide an encoding name and version
```

```xml
<Datamodel Format="helloworld" FormatVersion="1">
    <Datamodel.Root>
        <Element Name="my_root">
            <sys:String x:Key="Hello">World</sys:String>
        </Element>
    </Datamodel.Root>
</Datamodel>
```