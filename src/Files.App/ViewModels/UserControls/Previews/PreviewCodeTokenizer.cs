// Copyright (c) Files Community
// Licensed under the MIT License.

using ColorCode;
using ColorCode.Parsing;
using ColorCode.Styling;
using System;
using System.Collections.Generic;
using System.IO;

namespace Files.App.ViewModels.Previews
{
	public sealed class PreviewCodeTokenizer : CodeColorizerBase
	{
		public sealed record Token(string Text, string? ScopeName);

		public PreviewCodeTokenizer() : base(StyleDictionary.DefaultLight, null) { }

		public List<Token> Tokenize(string text, ILanguage language)
		{
			var tokens = new List<Token>();
			languageParser.Parse(text, language, (fragment, scopes) =>
			{
				// ColorCode's offsets are relative to each callback fragment.
				var position = 0;
				foreach (var scope in scopes)
				{
					if (scope.Index < position || scope.Length < 0 || scope.Index > fragment.Length - scope.Length)
						continue;
					if (scope.Index > position)
						tokens.Add(new Token(fragment[position..scope.Index], null));
					tokens.Add(new Token(fragment.Substring(scope.Index, scope.Length), scope.Name));
					position = scope.Index + scope.Length;
				}
				if (position < fragment.Length)
					tokens.Add(new Token(fragment[position..], null));
				if (tokens.Count > 8192)
					throw new InvalidDataException("The preview token limit was exceeded.");
			});
			return tokens;
		}

		protected override void Write(string parsedSourceCode, IList<Scope> scopes) { }
	}
}
