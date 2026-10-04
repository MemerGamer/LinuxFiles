// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
// C#/WinRT-only attribute; Uno has no WinRT projection so the attribute is a no-op marker there.
namespace WinRT
{
	[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
	internal sealed class DynamicWindowsRuntimeCastAttribute : Attribute
	{
		public DynamicWindowsRuntimeCastAttribute(Type type) { }
	}
}
#endif
