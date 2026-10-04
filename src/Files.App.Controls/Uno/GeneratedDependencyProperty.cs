// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
namespace CommunityToolkit.WinUI
{
	/// <summary>
	/// Uno-side declaration of the CommunityToolkit.Labs <c>[GeneratedDependencyProperty]</c> attribute. Implemented by
	/// <c>UnoDependencyPropertyGenerator</c> in Files.Core.SourceGenerator.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
	internal sealed class GeneratedDependencyPropertyAttribute : Attribute
	{
		public object? DefaultValue { get; init; }
	}
}
#endif
