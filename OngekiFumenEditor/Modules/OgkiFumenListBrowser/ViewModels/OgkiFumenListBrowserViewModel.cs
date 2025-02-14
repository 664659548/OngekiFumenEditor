﻿using Caliburn.Micro;
using Gemini.Framework;
using Gemini.Framework.Services;
using OngekiFumenEditor.Kernel.Audio;
using OngekiFumenEditor.Kernel.RecentFiles;
using OngekiFumenEditor.Modules.FumenVisualEditor;
using OngekiFumenEditor.Modules.FumenVisualEditor.Models;
using OngekiFumenEditor.Modules.OgkiFumenListBrowser.Models;
using OngekiFumenEditor.Parser;
using OngekiFumenEditor.Properties;
using OngekiFumenEditor.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using System.Xml.XPath;

namespace OngekiFumenEditor.Modules.OgkiFumenListBrowser.ViewModels
{
	[Export(typeof(IOgkiFumenListBrowser))]
	public class OgkiFumenListBrowserViewModel : WindowBase, IOgkiFumenListBrowser
	{
		private readonly static EnumerationOptions enumOpt = new EnumerationOptions()
		{
			MatchCasing = MatchCasing.CaseInsensitive,
		};

		private List<OngekiFumenSet> fumenSets = new();

		private string rootFolderPath = string.Empty;
		public string RootFolderPath
		{
			get => rootFolderPath;
			set
			{
				Set(ref rootFolderPath, value);
				RefreshList();
			}
		}

		private bool isBusy = false;
		public bool IsBusy
		{
			get => isBusy;
			set
			{
				Set(ref isBusy, value);
			}
		}

		private string keywords = string.Empty;
		public string Keywords
		{
			get => keywords;
			set
			{
				Set(ref keywords, value);
			}
		}

		public ObservableCollection<OngekiFumenSet> DisplayFumenSets { get; } = new ObservableCollection<OngekiFumenSet>();

		public OgkiFumenListBrowserViewModel()
		{
			DisplayName = Resource.OgkiFumenListBrowser;
			rootFolderPath = Properties.OptionGeneratorToolsSetting.Default.LastLoadedGameFolder;
		}

		protected override void OnViewLoaded(object view)
		{
			base.OnViewLoaded(view);
			if (Directory.Exists(RootFolderPath))
				RefreshList();
		}

		private async void RefreshList()
		{
			IsBusy = true;
			fumenSets.Clear();
			DisplayFumenSets.Clear();
			var resourceMap = new Dictionary<string, string>();
			var folder = await BuildFolder(RootFolderPath, resourceMap);

			IEnumerable<OngekiFumenSet> GetSet(Folder folder) =>
				folder.FumenSets.Concat(folder.SubFolders.SelectMany(x => GetSet(x)));

			Parallel.ForEach(GetSet(folder), s =>
			{
				s.JacketFilePath = resourceMap.TryGetValue("asset_" + s.MusicId, out var j) ? j : s.JacketFilePath;
				s.AudioFilePath = resourceMap.TryGetValue("audio_" + s.MusicSourceId, out var a) ? a : s.AudioFilePath;
			});

			fumenSets.AddRange(GetSet(folder).OrderBy(x => x.MusicId).DistinctContinuousBy(x => x.MusicId));
			IsBusy = false;

			ApplyKeywords();
		}

		public void ApplyKeywords()
		{
			var keyword = Keywords?.ToLowerInvariant();

			static int LevenshteinDistance(string s1, string s2)
			{
				if (s1.Contains(s2, StringComparison.InvariantCultureIgnoreCase) || s2.Contains(s1, StringComparison.InvariantCultureIgnoreCase))
					return 0;

				int[,] dp = new int[s1.Length + 1, s2.Length + 1];

				for (int i = 0; i <= s1.Length; i++)
				{
					dp[i, 0] = i;
				}

				for (int j = 0; j <= s2.Length; j++)
				{
					dp[0, j] = j;
				}

				for (int i = 1; i <= s1.Length; i++)
				{
					for (int j = 1; j <= s2.Length; j++)
					{
						int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
						dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
					}
				}

				return dp[s1.Length, s2.Length];
			}

			int fuzzyCalc(IEnumerable<string> strs)
			{
				var goodVal = int.MaxValue;

				foreach (var str in strs)
				{
					var modCount = LevenshteinDistance(str.ToLowerInvariant(), keyword);
					goodVal = Math.Min(modCount, goodVal);
				}

				return goodVal;
			}

			DisplayFumenSets.Clear();

			var result = string.IsNullOrWhiteSpace(keyword) ? fumenSets : fumenSets.Select(r =>
			{
				var provideStrings = new[]
				{
					r.Artist,
					r.Title,
				}.Concat(r.Difficults.Select(x => x.Creator));

				var q = fuzzyCalc(provideStrings);

				return (q, r);
			}).Where(x => x.q < 5).OrderBy(x => x.q).Select(x => x.r);

			DisplayFumenSets.AddRange(result);
		}

		public void SelectFolder()
		{
			if (!FileDialogHelper.OpenDirectory(Resource.SelectGameRootFolder, out var folderPath))
				return;

			RootFolderPath = folderPath;
			Properties.OptionGeneratorToolsSetting.Default.LastLoadedGameFolder = RootFolderPath;
			Properties.OptionGeneratorToolsSetting.Default.Save();
		}

		public async Task<Folder> BuildFolder(string folderPath, Dictionary<string, string> resourceMap)
		{
			var folder = new Folder();
			folder.Name = Path.GetFileName(folderPath);

			var subFolderPaths = Directory.GetDirectories(folderPath);

			await Task.WhenAll(subFolderPaths.Select(subFolderPath => Task.Run(async () =>
			{
				var subFolder = await BuildFolder(subFolderPath, resourceMap);
				if (!subFolder.IsEmpty)
					folder.SubFolders.Add(subFolder);
			})).ToArray());

			var folderName = Path.GetFileName(folderPath).ToLowerInvariant();

			if (folderName == "music")
			{
				var musicFilePaths = Directory.GetFiles(folderPath, "Music.xml", SearchOption.AllDirectories);
				foreach (var musicFilePath in musicFilePaths)
				{
					if ((await BuildFumenSet(musicFilePath)) is OngekiFumenSet set)
						folder.FumenSets.Add(set);
				}
			}
			else if (folderName == "musicsource")
			{
				var musicSourceFilePaths = Directory.GetFiles(folderPath, "MusicSource.xml", SearchOption.AllDirectories);
				foreach (var musicSourceFilePath in musicSourceFilePaths)
				{
					await BuildMusicSource(musicSourceFilePath, resourceMap);
				}
			}
			else if (folderName == "assets")
			{
				var files = Directory.GetFiles(folderPath, "ui_jacket_*");
				foreach (var file in files)
					BuildAssets(file, resourceMap);
			}

			return folder;
		}

		private void BuildAssets(string file, Dictionary<string, string> resourceMap)
		{
			if (!int.TryParse(Path.GetFileName(file).Replace("ui_jacket_", string.Empty), out var id))
				return;
			lock (resourceMap)
			{
				resourceMap["asset_" + id] = file;
			}
		}

		private async Task BuildMusicSource(string musicSourceFilePath, Dictionary<string, string> resourceMap)
		{
			using var fs = File.OpenRead(musicSourceFilePath);
			var musicXml = await XDocument.LoadAsync(fs, LoadOptions.None, default);

			string GetString(string name, string sub = "str")
			{
				var element = musicXml.XPathSelectElement($@"//{name}[1]/{sub}[1]");
				if (element?.Value is string strValue)
					return strValue;
				return string.Empty;
			}

			if (!int.TryParse(GetString("Name", "id"), out var id))
				return;

			var acbFileName = GetString("acbFile", "path");
			var acbFilePath = Path.Combine(Path.GetDirectoryName(musicSourceFilePath), acbFileName);

			if (!File.Exists(acbFilePath))
				return;

			lock (resourceMap)
			{
				resourceMap["audio_" + id] = acbFilePath;
			}
		}

		private async Task<OngekiFumenSet> BuildFumenSet(string musicXmlFilePath)
		{
			if (!File.Exists(musicXmlFilePath))
				return null;

			using var fs = File.OpenRead(musicXmlFilePath);
			var musicXml = await XDocument.LoadAsync(fs, LoadOptions.None, default);

			string GetString(string name)
			{
				var element = musicXml.XPathSelectElement($@"//{name}[1]/str[1]");
				if (element?.Value is string strValue)
					return strValue;
				return string.Empty;
			}

			int GetId(string name)
			{
				var element = musicXml.XPathSelectElement($@"//{name}[1]/id[1]");
				if (element?.Value is string strValue)
					return int.Parse(strValue);
				return 0;
			}

			var set = new OngekiFumenSet();
			set.Title = GetString("Name");
			set.Artist = GetString("ArtistName");
			set.Genre = GetString("Genre");
			set.MusicId = GetId("Name");
			set.MusicSourceId = GetId("MusicSourceName");

			var folderPath = Path.GetDirectoryName(musicXmlFilePath);

			foreach ((var fumenDataElement, var idx) in musicXml.XPathSelectElements("/MusicData/FumenData/FumenData").WithIndex())
			{
				string fumenConstIntegerPart = fumenDataElement.Element("FumenConstIntegerPart").Value;
				string fumenConstFractionalPart = fumenDataElement.Element("FumenConstFractionalPart").Value;
				string fumenFileName = fumenDataElement.Element("FumenFile").Element("path")?.Value;

				var fumenFilePath = Path.Combine(folderPath, fumenFileName);
				if (!File.Exists(fumenFilePath))
					continue;
				var fumenDiff = new OngekiFumenDiff(set);
				fumenDiff.DiffIdx = idx;
				fumenDiff.FilePath = fumenFilePath;
				fumenDiff.Level = (int.TryParse(fumenConstIntegerPart, out var d1) ? d1 : 0) + ((int.TryParse(fumenConstFractionalPart, out var d2) ? d2 : 0) / 100.0f);

				await ParseFumenFileInfo(fumenDiff);

				set.Difficults.Add(fumenDiff);
			}

			return set.Difficults.Count > 0 ? set : default;
		}

		private static Regex BpmRegex = new Regex(@"BPM_DEF\s*([\d\.]+)");
		private static Regex CreatorRegex = new Regex(@"CREATOR\s*(.+)");

		private async Task ParseFumenFileInfo(OngekiFumenDiff fumenDiff)
		{
			try
			{
				using var fs = File.OpenRead(fumenDiff.FilePath);
				using var reader = new StreamReader(fs);

				var isBpmSetup = false;
				var isCreatorSetup = false;

				while (!reader.EndOfStream)
				{
					var line = await reader.ReadLineAsync();

					if (!isBpmSetup)
					{
						var match = BpmRegex.Match(line);
						if (match.Success)
						{
							var bpm = float.Parse(match.Groups[1].Value);
							isBpmSetup = true;

							fumenDiff.Bpm = bpm;
						}
					}

					if (!isCreatorSetup)
					{
						var match = CreatorRegex.Match(line);
						if (match.Success)
						{
							var creator = match.Groups[1].Value;
							isCreatorSetup = true;

							fumenDiff.Creator = creator;
						}
					}

					if (isBpmSetup && isCreatorSetup)
						break;
				}
			}
			catch (Exception e)
			{
				//todo
			}
		}

		public async void LoadFumen(OngekiFumenDiff diff)
		{
			IsBusy = true;
			using var fs = File.OpenRead(diff.FilePath);
			var fumen = await IoC.Get<IFumenParserManager>().GetDeserializer(diff.FilePath).DeserializeAsync(fs);

			var newProj = new EditorProjectDataModel();
			newProj.FumenFilePath = diff.FilePath;
			newProj.Fumen = fumen;
			newProj.AudioFilePath = diff.RefSet.AudioFilePath;

			using var audio = await IoC.Get<IAudioManager>().LoadAudioAsync(diff.RefSet.AudioFilePath);
			if (audio is null)
			{
				MessageBox.Show(Resource.CantOpenByAudioFileNotFound.Format(diff.RefSet.Title));
				return;
			}
			newProj.AudioDuration = audio.Duration;

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
					var docName = $"[{Resource.FastOpen}] {diff.RefSet.Title}";

					editor.DisplayName = docName;
					IoC.Get<IEditorRecentFilesManager>().PostRecord(new(diff.FilePath, docName, RecentOpenType.CommandOpen));
				};
				frameworkElement.Loaded += loadedHandler;
			};

			await IoC.Get<IShell>().OpenDocumentAsync(editor);
			IsBusy = false;
		}
	}
}
