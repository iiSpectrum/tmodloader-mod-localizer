﻿using System;
using System.IO;
using Microsoft.Extensions.CommandLineUtils;
using ModLocalizer.ModLoader;

namespace ModLocalizer
{
	internal class Program
	{
		private static void Main(string[] args)
		{
			if (args.Length == 0)
			{
				args = new[]
				{
					@"C:\Users\zitwa\Source\ThoriumMod.tmod"
				};
			}

			var app = new CommandLineApplication(false)
			{
				FullName = typeof(Program).Namespace,
				ShortVersionGetter = () => typeof(Program).Assembly.GetName().Version.ToString(2),
				LongVersionGetter = () => typeof(Program).Assembly.GetName().Version.ToString(3)
			};

			var version = typeof(Program).Assembly.GetName().Version;
			app.HelpOption("--help | -h");
			app.VersionOption("-v | --version", version.ToString(2), version.ToString(3));

			var pathArgument = app.Argument("Mod path", "mod path");
			var modeOption = app.Option("-m | --mode", "program mode", CommandOptionType.SingleValue);
			var folderOption = app.Option("-f | --folder", "mod localized content folder", CommandOptionType.SingleValue);

			var dump = true;
			string folder = null, path = null;

			app.OnExecute(() =>
			{
				if (modeOption.HasValue())
				{
					dump = !string.Equals(modeOption.Value(), "patch", StringComparison.OrdinalIgnoreCase);
				}

				if (folderOption.HasValue())
				{
					folder = folderOption.Value();
				}

				path = pathArgument.Value;
				if (string.IsNullOrWhiteSpace(path))
				{
					Console.WriteLine("Please specify the mod file to be processed.");
					Environment.Exit(1);
				}

				if (!dump && string.IsNullOrWhiteSpace(folder))
				{
					Console.WriteLine("Please specify the content folder for mod patching.");
					Environment.Exit(1);
				}
				
				return 0;
			});
			app.Execute(args);

			ProcessInput(path, folder, dump);
		}

		private static void ProcessInput(string modPath, string contentFolderPath, bool dump = true)
		{
			if (string.IsNullOrWhiteSpace(modPath))
				throw new ArgumentException("Value cannot be null or whitespace.", nameof(modPath));

			if(!dump && string.IsNullOrWhiteSpace(contentFolderPath))
				throw new ArgumentException("Value cannot be null or whitespace.", nameof(contentFolderPath));

			if (!File.Exists(modPath))
			{
				Console.WriteLine("mod file does not exist");
				return;
			}

			var modFile = new TmodFile(modPath);
			modFile.Read();

			if (dump)
			{
				new Dumper(modFile).Run();
			}
			else
			{
				new Patcher(modFile, contentFolderPath).Run();
			}
		}
	}
}