// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Mime
{
	/// <summary>A desktop action applicable to the entire selection. Discovery never executes it.</summary>
	public sealed record ServiceMenuAction(string ActionId, DesktopApplication Application, string? Submenu, string Priority);

	/// <summary>Discovers desktop service-menu actions for paths or URIs.</summary>
	public interface IServiceMenuService
	{
		Task<IReadOnlyList<ServiceMenuAction>> GetActionsAsync(IReadOnlyList<string> targets, CancellationToken cancellationToken = default);
	}
}
