// Copyright (c) Files Community
// Licensed under the MIT License.

// C#/WinRT-only attribute; Uno has no WinRT projection so the attribute is a no-op marker there.
namespace WinRT
{
	[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
	internal sealed class DynamicWindowsRuntimeCastAttribute : Attribute
	{
		public DynamicWindowsRuntimeCastAttribute(Type type) { }
	}
}

namespace WinRT
{
	/// <summary>
	/// C#/WinRT-only attribute (generates ICustomProperty support for x:Bind/Binding on WinUI); no-op on Uno, which binds through reflection.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
	internal sealed class GeneratedBindableCustomPropertyAttribute : Attribute
	{
		public GeneratedBindableCustomPropertyAttribute() { }

		public GeneratedBindableCustomPropertyAttribute(string[] propertyNames, string[] indexerPropertyTypes) { }
	}
}

namespace WinRT
{
	// C#/WinRT AOT marshalling hints; no-ops on Uno.
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate, AllowMultiple = true)]
	internal sealed class GeneratedWinRTExposedTypeAttribute : Attribute
	{
	}

	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	internal sealed class GeneratedWinRTExposedExternalTypeAttribute : Attribute
	{
		public GeneratedWinRTExposedExternalTypeAttribute(Type type) { }
	}
}
