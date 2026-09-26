//
// SystemXamlShim.cs — .NET modern compatibility shim for the Xwt frontend.
//
// The Xwt object model (XwtXamlWriter serialization path) uses three types
// from System.Xaml: ContentPropertyAttribute, ValueSerializer and
// IValueSerializerContext. System.Xaml does not ship with modern .NET, so
// for net10.0 builds the types are provided here with the exact same public
// contract the Xwt value serializers (Color/Font/Size/Cursor/WidgetSpacing)
// rely on: metadata-only attributes and a serializer base class with the
// four conversion overrides. On net40 the real System.Xaml types win
// (this file is excluded by the NET define), keeping bit-for-bit parity
// with the legacy builds.
//
// Part of the local .NET 10 fork port (see Directory.Build.targets history).
//

#if NET

using System.ComponentModel;

namespace System.Xaml
{
	/// <summary>
	/// Compatibility marker: keeps the legacy 'using System.Xaml;' directives
	/// of Widget.cs / WidgetSpacing.cs / DesignerSurface.cs valid on modern
	/// .NET builds (the real namespace only exists with the framework's
	/// System.Xaml.dll). The shim types live in System.Windows.Markup.
	/// </summary>
	public static class NamespaceMarker
	{
	}

#if !NET40
	/// <summary>
	/// XAML object services (System.Xaml.XamlServices parity for the
	/// DesignerSurface load/save API). The real System.Xaml pipeline does not
	/// ship with modern .NET; the designer surface reports the limitation
	/// instead of failing to compile.
	/// </summary>
	public static class XamlServices
	{
		public static object Load (System.Xml.XmlReader reader)
			=> throw new PlatformNotSupportedException (
				"Xwt designer XAML serialization requires System.Xaml (.NET Framework builds). Not available on this target.");

		public static void Save (System.Xml.XmlWriter writer, object instance)
			=> throw new PlatformNotSupportedException (
				"Xwt designer XAML serialization requires System.Xaml (.NET Framework builds). Not available on this target.");
	}
#endif
}

namespace System.Windows.Markup
{
	/// <summary>
	/// Declares the property that receives the implicit content of a type
	/// (same name/shape as System.Windows.Markup.ContentPropertyAttribute).
	/// </summary>
	[AttributeUsage (AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
	public sealed class ContentPropertyAttribute : Attribute
	{
		public string Name { get; }

		public ContentPropertyAttribute (string name) => Name = name;
	}

	/// <summary>
	/// Base class for the Xwt value serializers (same abstract surface as
	/// System.Windows.Markup.ValueSerializer restricted to the overrides the
	/// Xwt serializers implement).
	/// </summary>
	public abstract class ValueSerializer
	{
		public virtual bool CanConvertFromString (string value, IValueSerializerContext context) => true;

		public virtual bool CanConvertToString (object value, IValueSerializerContext context) => false;

		public virtual string ConvertToString (object value, IValueSerializerContext context)
			=> value?.ToString () ?? string.Empty;

		public virtual object ConvertFromString (string value, IValueSerializerContext context) => value;
	}

	/// <summary>
	/// Context passed to ValueSerializer overrides (same member set as
	/// System.Xaml's IValueSerializerContext for Xwt usage: the type
	/// descriptor surface).
	/// </summary>
	public interface IValueSerializerContext : ITypeDescriptorContext
	{
	}
}

#endif
