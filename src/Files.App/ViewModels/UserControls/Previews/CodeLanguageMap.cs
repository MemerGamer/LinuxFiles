// Copyright (c) Files Community
// Licensed under the MIT License.

using ColorCode;
using System.Collections.Generic;
using System.Collections.Frozen;

namespace Files.App.ViewModels.Previews
{
	/// <summary>
	/// Maps file extensions to ColorCode.Core languages for the highlighted text preview.
	/// </summary>
	public static class CodeLanguageMap
	{
		private static readonly FrozenDictionary<string, ILanguage> map = Build();

		public static bool TryGetLanguage(string? extension, out ILanguage language)
		{
			language = null!;
			return extension is not null && map.TryGetValue(extension.ToLowerInvariant(), out language!);
		}

		private static FrozenDictionary<string, ILanguage> Build()
		{
			var items = new (ILanguage Language, string Extensions)[]
			{
				(Languages.Aspx, "aspx"),
				(Languages.Cpp, "c,cpp,c++,cc,cp,cxx,h,h++,hh,hpp,hxx,inc,inl,ino,ipp,tcc,tpp"),
				(Languages.CSharp, "cs,cake,csx,linq"),
				(Languages.Css, "css,scss"),
				(Languages.FSharp, "fs,fsi,fsx"),
				(Languages.Haskell, "hs"),
				(Languages.Html, "html,htm,razor,cshtml,vbhtml,svelte"),
				(Languages.Java, "java"),
				(Languages.JavaScript, "js,jsx,mjs,cjs"),
				(Languages.JavaScript, "json,jsonc"),
				(Languages.Python, "py,pyw,pyi"),
				(Languages.Sql, "sql"),
				(Languages.Php, "php"),
				(Languages.PowerShell, "pwsh,ps1,psd1,psm1"),
				(Languages.Typescript, "ts,tsx"),
				(Languages.VbDotNet, "vb,vbs"),
				// LINUX-TODO(preview): show SVG source until external references can be isolated during rasterization.
				(Languages.Xml, "xml,svg,axml,xaml,xsd,xsl,xslt,xlf,csproj,props,targets,resx,plist"),
			};

			var dictionary = new Dictionary<string, ILanguage>();
			foreach (var (language, extensions) in items)
				foreach (var ext in extensions.Split(','))
					dictionary[$".{ext}"] = language;

			return dictionary.ToFrozenDictionary();
		}
	}
}
