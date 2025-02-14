﻿using Caliburn.Micro;
using Gemini.Framework.Results;
using Gemini.Framework.Services;
using OngekiFumenEditor.Kernel.Audio;
using OngekiFumenEditor.Kernel.RecentFiles;
using OngekiFumenEditor.Modules.FumenVisualEditor;
using OngekiFumenEditor.Modules.FumenVisualEditor.Models;
using OngekiFumenEditor.Parser;
using OngekiFumenEditor.Properties;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Xml.XPath;

namespace OngekiFumenEditor.Utils
{
	internal static class DocumentOpenHelper
	{
		public static async Task<bool> TryOpenAsDocument(string filePath)
		{
			if (IoC.GetAll<IEditorProvider>().FirstOrDefault(x => x.Handles(filePath)) is IEditorProvider provider)
			{
				Log.LogInfo($"通过命令行快速打开文档:({provider}) {filePath}");
				await Dispatcher.Yield();
				var openDocument = Show.Document(filePath);
				await Coroutine.ExecuteAsync(new IResult[] { openDocument }.AsEnumerable().GetEnumerator());
				return true;
			}
			else if (filePath.EndsWith(".ogkr") || filePath.EndsWith(".nyageki"))
			{
				return await TryOpenOgkrFileAsDocument(filePath);
			}

			return false;
		}

		public static async Task<bool> TryOpenOgkrFileAsDocument(string ogkrFilePath)
		{
			var newProj = await TryCreateEditorProjectDataModel(ogkrFilePath);
			if (newProj is null)
				return false;

			var fumenProvider = IoC.Get<IFumenVisualEditorProvider>();
			var editor = IoC.Get<IFumenVisualEditorProvider>().Create();
			var viewAware = (IViewAware)editor;
			viewAware.ViewAttached += (sender, e) =>
			{
				var frameworkElement = (FrameworkElement)e.View;

				RoutedEventHandler loadedHandler = null;
				loadedHandler = async (sender2, e2) =>
				{
					frameworkElement.Loaded -= loadedHandler;
					await fumenProvider.Open(editor, newProj);
					var docName = await TryFormatOpenFileName(ogkrFilePath);

					editor.DisplayName = docName;
					IoC.Get<IEditorRecentFilesManager>().PostRecord(new(ogkrFilePath, docName, RecentOpenType.CommandOpen));
				};
				frameworkElement.Loaded += loadedHandler;
			};

			await IoC.Get<IShell>().OpenDocumentAsync(editor);
			return true;
		}

		public static async Task<bool> TryOpenProject(EditorProjectDataModel proj)
		{
			var fumenProvider = IoC.Get<IFumenVisualEditorProvider>();
			var editor = IoC.Get<IFumenVisualEditorProvider>().Create();
			var viewAware = (IViewAware)editor;
			viewAware.ViewAttached += (sender, e) =>
			{
				var frameworkElement = (FrameworkElement)e.View;

				RoutedEventHandler loadedHandler = null;
				loadedHandler = async (sender2, e2) =>
				{
					frameworkElement.Loaded -= loadedHandler;
					await fumenProvider.Open(editor, proj);
				};
				frameworkElement.Loaded += loadedHandler;
			};

			await IoC.Get<IShell>().OpenDocumentAsync(editor);
			return true;
		}

		public static async Task<EditorProjectDataModel> TryCreateEditorProjectDataModel(string ogkrFilePath)
		{
			(var audioFile, var audioDuration) = await GetAudioFilePath(ogkrFilePath);

			if (!File.Exists(audioFile))
			{
				audioFile = FileDialogHelper.OpenFile(Resource.SelectAudioFileManually, IoC.Get<IAudioManager>().SupportAudioFileExtensionList);
				if (!File.Exists(audioFile))
					return null;
				audioDuration = await CalcAudioDuration(audioFile);
			}

			using var fs = File.OpenRead(ogkrFilePath);
			var fumen = await IoC.Get<IFumenParserManager>().GetDeserializer(ogkrFilePath).DeserializeAsync(fs);

			var newProj = new EditorProjectDataModel();
			newProj.FumenFilePath = ogkrFilePath;
			newProj.Fumen = fumen;
			newProj.AudioFilePath = audioFile;
			newProj.AudioDuration = audioDuration;

			return newProj;
		}

		public static async Task<string> TryFormatOpenFileName(string ogkrFilePath)
		{
			var result = Path.GetFileName(ogkrFilePath);

			var ogkrFileDir = Path.GetDirectoryName(ogkrFilePath);
			var musicXmlFilePath = Path.Combine(ogkrFileDir, "Music.xml");

			//从Music.xml读取musicId
			if (File.Exists(musicXmlFilePath))
			{
				var musicXml = await XDocument.LoadAsync(File.OpenRead(musicXmlFilePath), LoadOptions.None, default);
				var element = musicXml.XPathSelectElement(@"//Name[1]/str[1]");
				if (element?.Value is string name)
					result = name;
			}

			return $"[{Resource.FastOpen}] " + result;
		}

		private static async Task<(string, TimeSpan)> GetAudioFilePath(string ogkrFilePath)
		{
			var ogkrFileDir = Path.GetDirectoryName(ogkrFilePath);
			var musicXmlFilePath = Path.Combine(ogkrFileDir, "Music.xml");
			var musicId = -2857;

			if (File.Exists(musicXmlFilePath))
			{
				//从Music.xml读取musicId
				var musicXml = await XDocument.LoadAsync(File.OpenRead(musicXmlFilePath), LoadOptions.None, default);
				var element = musicXml.XPathSelectElement(@"//MusicSourceName[1]/id[1]");
				if (element != null)
				{
					musicId = int.Parse(element.Value);
				}
			}

			if (musicId < 0)
			{
				//从文件名读取musicId
				var match = new Regex(@"(\d+)_\d+").Match(Path.GetFileNameWithoutExtension(ogkrFilePath));
				if (match.Success)
				{
					musicId = int.Parse(match.Groups[1].Value);
				}
			}

			if (musicId < 0)
			{
				return default;
			}

			var musicIdStr = musicId < 1000 ? string.Concat("0".Repeat(4 - musicId.ToString().Length)) + musicId : musicId.ToString();

			var musicSourcePath = Path.GetFullPath(Path.Combine(ogkrFileDir, "..", "..", "musicsource", $"musicsource{musicIdStr}"));
			var audioExts = IoC.Get<IAudioManager>().SupportAudioFileExtensionList.Select(x => x.fileExt.TrimStart('.')).ToArray();
			var audioFile = "";

			if (Directory.Exists(musicSourcePath))
			{
				//去对应的musicsource文件夹检查
				audioFile = Directory.GetFiles(musicSourcePath, $"music{musicIdStr}.*").Where(x => audioExts.Any(t => x.EndsWith(t))).FirstOrDefault();
			}

			if (!File.Exists(audioFile))
			{
				return default;
			}

			return (audioFile, await CalcAudioDuration(audioFile));
		}

		private static async Task<TimeSpan> CalcAudioDuration(string audioFilePath)
		{
			using var audio = await IoC.Get<IAudioManager>().LoadAudioAsync(audioFilePath);
			return audio.Duration;
		}
	}
}
