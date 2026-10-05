// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Clipboard;
using Microsoft.Extensions.DependencyInjection;

namespace Files.Platform.Linux.Clipboard
{
	/// <summary>
	/// Provides extension methods for registering the Linux clipboard and drag source.
	/// </summary>
	public static class ClipboardServiceCollectionExtensions
	{
		/// <summary>
		/// Registers one X11 implementation behind <see cref="IClipboardService"/> and <see cref="IFileDragSource"/>.
		/// </summary>
		public static IServiceCollection AddLinuxClipboard(this IServiceCollection services)
		{
			return services
				.AddSingleton(_ => new LinuxClipboardService())
				.AddSingleton<IClipboardService>(sp => sp.GetRequiredService<LinuxClipboardService>())
				.AddSingleton<IFileDragSource>(sp => sp.GetRequiredService<LinuxClipboardService>());
		}
	}
}
