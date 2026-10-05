// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Actions
{
	internal abstract partial class BaseRunAsAction : ObservableObject, IAction
	{
		private readonly IContentPageContext _context;

		private readonly string _verb;

		public abstract string Label { get; }

		public abstract string Description { get; }

		public virtual ActionCategory Category
			=> ActionCategory.Run;

		public abstract RichGlyph Glyph { get; }

		public virtual bool IsExecutable { get; }

		public BaseRunAsAction(string verb)
		{
			_verb = verb;
			_context = Ioc.Default.GetRequiredService<IContentPageContext>();

			_context.PropertyChanged += Context_PropertyChanged;
		}

#if !WINDOWS
		// LINUX-TODO(launching): Windows shell elevation verbs are hidden on Linux.
		public Task ExecuteAsync(object? parameter = null)
			=> Task.CompletedTask;
#endif

		public void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.SelectedItems):
				case nameof(IContentPageContext.Folder):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}
