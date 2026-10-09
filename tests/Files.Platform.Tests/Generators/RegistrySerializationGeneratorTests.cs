// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Linq;
using Files.Core.SourceGenerator.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Generators
{
	[TestClass]
	public sealed class RegistrySerializationGeneratorTests
	{
		[TestMethod]
		[DataRow(false)]
		[DataRow(true)]
		public void OnlyWindowsProducesRegistrySerializers(bool windows)
		{
			var options = new CSharpParseOptions(preprocessorSymbols: windows ? ["WINDOWS"] : ["DESKTOP"]);
			var source = CSharpSyntaxTree.ParseText("""
				namespace Files.Shared.Attributes
				{
					public sealed class RegistrySerializableAttribute : System.Attribute { }
				}
				namespace Example
				{
					[Files.Shared.Attributes.RegistrySerializable]
					public sealed class TaggedFile { public string FilePath { get; set; } }
				}
				""", options);
			var compilation = CSharpCompilation.Create("RegistryFixture", [source],
				[MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
			GeneratorDriver driver = CSharpGeneratorDriver.Create([new RegistrySerializationGenerator().AsSourceGenerator()], parseOptions: options);
			var result = driver.RunGenerators(compilation).GetRunResult();
			Assert.IsEmpty(result.Diagnostics);
			Assert.AreEqual(windows ? 1 : 0, result.GeneratedTrees.Length);
			if (windows)
			{
				var generated = result.GeneratedTrees.Single().ToString();
				StringAssert.Contains(generated, "class TaggedFileRegistry");
				StringAssert.Contains(generated, "using Microsoft.Win32;");
			}
		}
	}
}
