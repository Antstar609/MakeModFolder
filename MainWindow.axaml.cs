using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Models;

namespace KCDModBuilder;

public partial class MainWindow : Window
{
	public bool IsSilent = false;

	private readonly PresetData m_presetData;
	private readonly ModManifestWriter m_modManifestWriter;

	private const string m_GameExePath1 = @"\Bin\Win64\KingdomCome.exe";
	private const string m_GameExePath2 = @"\Bin\Win64MasterMasterSteamPGO\KingdomCome.exe";
	private string m_modPath = "";

	public MainWindow()
	{
		InitializeComponent();

		m_presetData = new PresetData(this);
		m_modManifestWriter = new ModManifestWriter(this);

		CheckCanRun(null, null);
		m_presetData.LoadAllPresets();
		m_presetData.LoadLastPreset();
	}

	private async Task MakeModFolderAsync()
	{
		m_modPath = Path.Combine(xGamePath.Text, "Mods", xModName.Text);
		Directory.CreateDirectory(m_modPath);

		CopyModdingEula();
		ZipDirectories();
		m_modManifestWriter.WriteModManifest();
		CompressArchive();

		if (!IsSilent)
		{
			m_presetData.StorePresetData();
			m_presetData.LoadAllPresets();
			xPresets.SelectedItem = xModName.Text;
			xPresets.IsEnabled = true;
		}

		await DisplayMessageAsync("The mod folder has been created at " + m_modPath);
	}

	private void CopyModdingEula()
	{
		string targetFilePath = Path.Combine(m_modPath, "modding_eula.txt");
		if (File.Exists(targetFilePath)) return;

		using Stream resourceStream = AssetLoader.Open(new Uri("avares://KCDModBuilder/Assets/modding_eula.txt"));
		using FileStream fileStream = new FileStream(targetFilePath, FileMode.CreateNew, FileAccess.Write);

		resourceStream.CopyTo(fileStream);
	}

	private void ZipDirectories()
	{
		var dataDir = Directory.CreateDirectory(Path.Combine(m_modPath, "Data"));
		string zipFilePath = Path.Combine(dataDir.FullName, xModName.Text.Replace(" ", "_").ToLower() + ".pak");
		var projectDirs = Directory.GetDirectories(xProjectPath.Text);

		string[] allowedFolders =
		{
			"Scripts", "Objects", "Libs", "Entities"
		};

		using ZipArchive archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);
		foreach (var dir in projectDirs)
		{
			string dirName = Path.GetFileName(dir);

			if (allowedFolders.Contains(dirName, StringComparer.OrdinalIgnoreCase))
			{
				AddDirectoryContentsToZip(archive, dir, dirName);
			}

			if (!dir.Contains("Localization")) continue;

			var localizationPath = Directory.CreateDirectory(Path.Combine(m_modPath, "Localization"));
			string[] localizationDirectories = Directory.GetDirectories(Path.GetFullPath(dir));
			foreach (string languageDir in localizationDirectories)
			{
				string languagePath = Path.Combine(localizationPath.FullName, Path.GetFileName(languageDir) + "_xml.pak");
				ZipFile.CreateFromDirectory(languageDir, languagePath, CompressionLevel.Optimal, false);
			}
		}
	}

	private void AddDirectoryContentsToZip(ZipArchive _archive, string _sourceDir, string _entryPath)
	{
		foreach (var file in Directory.GetFiles(_sourceDir))
		{
			string entryName = Path.Combine(_entryPath, Path.GetFileName(file));
			_archive.CreateEntryFromFile(file, entryName);
		}

		foreach (var subDir in Directory.GetDirectories(_sourceDir))
		{
			string subDirName = Path.GetFileName(subDir);
			string newEntryPath = Path.Combine(_entryPath, subDirName);
			AddDirectoryContentsToZip(_archive, subDir, newEntryPath);
		}
	}

	private void CompressArchive()
	{
		string archivePath = Path.Combine(xProjectPath.Text, "Archives");

		if (!Directory.Exists(archivePath))
		{
			Directory.CreateDirectory(archivePath);
		}

		string formatedName = xModName.Text.Replace(" ", "-");
		string archiveFileName = Path.Combine(archivePath, formatedName + "-v" + xModVersion.Text + ".zip");

		if (File.Exists(archiveFileName))
		{
			File.Delete(archiveFileName);
		}

		ZipFile.CreateFromDirectory(m_modPath, archiveFileName, CompressionLevel.Optimal, true);
	}

	private async Task ProjectBrowsePathAsync()
	{
		var topLevel = GetTopLevel(this);
		var options = new FolderPickerOpenOptions
		{
			Title = "Select the Project Folder",
			AllowMultiple = false
		};

		var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
		if (folders.Count <= 0) return;

		var selectedFolder = folders[0];
		xProjectPath.Text = selectedFolder.Path.LocalPath.TrimEnd(Path.DirectorySeparatorChar);
	}

	private async void ProjectBrowsePath_Click(object _sender, RoutedEventArgs _event)
	{
		await ProjectBrowsePathAsync();
	}

	private async Task GameBrowsePathAsync()
	{
		var topLevel = GetTopLevel(this);

		var folderUri = new Uri("file:///C:/Program Files (x86)/Steam/steamapps/common/");
		var suggestedFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(folderUri);

		var options = new FolderPickerOpenOptions
		{
			Title = "Select the Game Folder",
			AllowMultiple = false,
			SuggestedStartLocation = suggestedFolder
		};

		var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
		if (folders.Count <= 0) return;

		var selectedFolder = folders[0];
		string potentialGamePath = selectedFolder.Path.LocalPath.TrimEnd(Path.DirectorySeparatorChar);

		// Check if the selected directory is a valid game path
		string exePath1 = potentialGamePath + m_GameExePath1;
		string exePath2 = potentialGamePath + m_GameExePath2;

		if (File.Exists(exePath1) || File.Exists(exePath2))
		{
			xGamePath.Text = potentialGamePath;
		}
		else
		{
			await DisplayMessageAsync("The selected directory does not appear to be a valid game path. Please select the directory where Kingdom Come: Deliverance is installed.");
		}
	}

	private async void GameBrowsePath_Click(object _sender, RoutedEventArgs _event)
	{
		await GameBrowsePathAsync();
	}

	public async Task RunAsync()
	{
		if (!string.IsNullOrEmpty(xModName.Text) && !string.IsNullOrEmpty(xProjectPath.Text) && !string.IsNullOrEmpty(xGamePath.Text) &&
		    !string.IsNullOrEmpty(xModVersion.Text) && !string.IsNullOrEmpty(xAuthor.Text))
		{
			// Check if the mod already exists and if I can access it (if it's not in use)
			string modPath = Path.Combine(xGamePath.Text, "Mods", xModName.Text);
			if (Directory.Exists(modPath))
			{
				try
				{
					Directory.Delete(modPath, true);
				}
				catch (Exception)
				{
					await DisplayMessageAsync("Please close the game and try again!");
					return;
				}
			}

			await MakeModFolderAsync();
		}
		else
		{
			await DisplayMessageAsync("Please fill all the fields in the app before using the silent mode.");
		}
	}

	private async void Run_Click(object _sender, RoutedEventArgs _event)
	{
		await RunAsync();
	}

	private void CheckCanRun(object _sender, TextChangedEventArgs _e)
	{
		xRunButton.IsEnabled = !string.IsNullOrEmpty(xModName.Text)
		                       && !string.IsNullOrEmpty(xProjectPath.Text)
		                       && !string.IsNullOrEmpty(xGamePath.Text)
		                       && !string.IsNullOrEmpty(xModVersion.Text)
		                       && !string.IsNullOrEmpty(xAuthor.Text);
	}

	private void Presets_SelectionChanged(object _sender, SelectionChangedEventArgs _e)
	{
		if (xPresets.SelectedItem == null) return;

		string? selectedPreset = xPresets.SelectedItem.ToString();
		m_presetData.LoadPresetData(selectedPreset);

		if (xPresets.SelectedItem.ToString() == "No presets created")
		{
			xPresets.IsEnabled = false;
		}
	}

	private async Task DeletePresetAsync()
	{
		if (xPresets.SelectedItem == null || xPresets.SelectedItem.ToString() == "No presets created") return;

		string? selectedPreset = xPresets.SelectedItem.ToString();
		xPresets.Items.Remove(selectedPreset);
		m_presetData.DeletePreset(selectedPreset);
		await DisplayMessageAsync("Preset " + selectedPreset + " has been deleted.");
	}

	private async void DeletePreset_Click(object _sender, RoutedEventArgs _e)
	{
		await DeletePresetAsync();
	}

	private async Task DisplayMessageAsync(string _message)
	{
		if (IsSilent)
		{
			Console.WriteLine(_message);
		}
		else
		{
			if (_message.Contains("The mod folder has been created at"))
			{
				var messageBoxCustomParams = new MessageBoxCustomParams
				{
					ContentTitle = "KCD Mod Builder",
					ContentMessage = _message,
					WindowStartupLocation = WindowStartupLocation.CenterOwner,
					CanResize = false,
					ShowInCenter = true,
					ButtonDefinitions = new List<ButtonDefinition>
					{
						new()
						{
							Name = "Open Mod Location"
						},
						new()
						{
							Name = "Ok",
						}
					}
				};

				var result = await MessageBoxManager.GetMessageBoxCustom(messageBoxCustomParams).ShowAsync();
				if (result == "Open Mod Location")
				{
					System.Diagnostics.Process.Start("explorer.exe", m_modPath);
				}
			}
			else
			{
				await MessageBoxManager.GetMessageBoxStandard("KCD Mod Builder", _message).ShowAsync();
			}
		}
	}
}

public partial class NonSpecialCharTextBox : TextBox
{
	protected override Type StyleKeyOverride => typeof(TextBox);

	[GeneratedRegex(@"[^a-zA-Z0-9\s]", RegexOptions.Compiled)]
	private static partial Regex SpecialChar();

	private readonly static Regex SpecialCharRegex = SpecialChar();

	protected override void OnTextInput(TextInputEventArgs _event)
	{
		if (SpecialCharRegex.IsMatch(_event.Text))
		{
			_event.Handled = true;
			return;
		}

		base.OnTextInput(_event);
	}
}

public partial class NumbersOnlyTextBox : TextBox
{
	protected override Type StyleKeyOverride => typeof(TextBox);

	[GeneratedRegex("[^0-9.]+")]
	private static partial Regex NumberValidation();

	private readonly static Regex NumberRegex = NumberValidation();

	protected override void OnTextInput(TextInputEventArgs _event)
	{
		if (NumberRegex.IsMatch(_event.Text))
		{
			_event.Handled = true;
			return;
		}

		base.OnTextInput(_event);
	}
}