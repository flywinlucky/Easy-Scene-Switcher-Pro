using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlyStudiosGames.EasySceneSwitcherPro.Editor
{
	/// <summary>
	/// Dockable editor window used to browse and open scenes quickly.
	/// </summary>
	public sealed class EasySceneSwitcherWindow : EditorWindow
	{
		#region Constants
		private const string SourcePrefKey = "FlyStudiosGames.EasySceneSwitcherPro.SceneSource";
		private const string HideReadOnlyPrefKey = "FlyStudiosGames.EasySceneSwitcherPro.HideReadOnlyScenes";
		private const string FavoriteScenesPrefKey = "FlyStudiosGames.EasySceneSwitcherPro.FavoriteScenes";
		private const string RecentScenesPrefKey = "FlyStudiosGames.EasySceneSwitcherPro.RecentScenes";
		private const string RememberStartupScenePrefKey = "FlyStudiosGames.EasySceneSwitcherPro.RememberStartupScene";
		private const string SetActiveAfterAdditivePrefKey = "FlyStudiosGames.EasySceneSwitcherPro.SetActiveAfterAdditive";
		private const string StartupScenePathPrefKey = "FlyStudiosGames.EasySceneSwitcherPro.StartupScenePath";
		private const string PendingStartupRestorePrefKey = "FlyStudiosGames.EasySceneSwitcherPro.PendingStartupRestore";
		private const string CompanyName = "Fly Studios Games";
		private const string AssetStoreStoreUrl = "https://assetstore.unity.com/publishers/56140";
		private const int MaxRecentScenes = 20;
		#endregion

		#region GUI Content
		private static readonly GUIContent[] SourceOptions =
		{
			new GUIContent("Build Settings"),
			new GUIContent("All Project")
		};
		#endregion

		#region State
		private static readonly List<SceneEntry> SceneCache = new List<SceneEntry>();

		private static Vector2 _scrollPosition;
		private static string _searchText = string.Empty;
		private static bool _isDirty = true;
		private static SceneSource _sceneSource;
		private static bool _hideReadOnlyScenes;
		private static bool _rememberStartupScene;
		private static bool _setActiveAfterAdditive;
		private static string _startupScenePath = string.Empty;
		private static bool _pendingStartupRestore;
		private static EasySceneSwitcherWindow _window;
		private static GUIContent _playSceneContent;
		private static GUIContent _readOnlyContent;
		private static readonly HashSet<string> FavoriteScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static readonly List<string> RecentScenes = new List<string>();
		#endregion

		#region Types
		private enum SceneSource
		{
			BuildSettings = 0,
			AllProject = 1
		}

		private sealed class SceneEntry
		{
			public string FilePath;
			public string SceneName;
			public bool IsInBuildSettings;
			public bool IsEnabledInBuildSettings;
			public bool IsMissing;
			public bool IsReadOnly;
			public bool IsFavorite;
		}
		#endregion

		#region Menu
		[MenuItem("Tools/Easy Scene Switcher Pro/Open Window", priority = 10)]
 		private static void OpenWindow()
		{
			_window = GetWindow<EasySceneSwitcherWindow>("Easy Scene Switcher Pro");
			_window.minSize = new Vector2(340f, 240f);
			_window.Show();
		}

		#endregion

		#region Unity Events
		private void OnEnable()
		{
			_window = this;
			EnsureGuiContentInitialized();
			LoadPreferences();
			MarkDirty();

			EditorBuildSettings.sceneListChanged += MarkDirty;
			EditorApplication.projectChanged += MarkDirty;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		private void OnDisable()
		{
			if (_window == this)
				_window = null;

			EditorBuildSettings.sceneListChanged -= MarkDirty;
			EditorApplication.projectChanged -= MarkDirty;
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
		}

		private void OnGUI()
		{
			// Keep an active reference so list sizing remains adaptive after script reloads.
			if (_window == null)
				_window = this;

			EnsureGuiContentInitialized();
			SyncHideReadOnlyPreference();

			RefreshSceneCacheIfNeeded();
			bool isLockedInPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;
			bool compact = _window != null && _window.position.width < 420f;

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				DrawHeader();
				DrawToolbar(compact);

				if (isLockedInPlayMode)
					EditorGUILayout.HelpBox("In Play Mode, scene switching and delete are disabled. You can still ping scene assets.", MessageType.Warning);

				DrawSceneList(compact);
				GUILayout.FlexibleSpace();
				DrawFooterBranding();
			}
		}
		#endregion

		#region Drawing
		private static void DrawHeader()
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.Label($"Favorites: {FavoriteScenes.Count}", EditorStyles.miniLabel);
				GUILayout.Space(8f);
				GUILayout.Label($"Recent: {RecentScenes.Count}", EditorStyles.miniLabel);
				GUILayout.FlexibleSpace();
				GUILayout.Label($"Scenes: {SceneCache.Count}", EditorStyles.miniLabel);
			}

			EditorGUILayout.Space(2f);
		}

		private static void DrawToolbar(bool compact)
		{
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					EditorGUI.BeginChangeCheck();
					int selectedSource = GUILayout.Toolbar((int)_sceneSource, SourceOptions, EditorStyles.miniButton);
					if (EditorGUI.EndChangeCheck())
					{
						_sceneSource = (SceneSource)Mathf.Clamp(selectedSource, 0, SourceOptions.Length - 1);
						SavePreferences();
						MarkDirty();
					}

					if (GUILayout.Button("Refresh", GUILayout.Width(compact ? 64f : 74f)))
					{
						MarkDirty();
						RefreshSceneCacheIfNeeded(force: true);
					}
				}

				using (new EditorGUILayout.HorizontalScope())
				{
					EditorGUILayout.LabelField("Search", GUILayout.Width(52f));
					_searchText = EditorGUILayout.TextField(_searchText);

					if (GUILayout.Button("Clear", GUILayout.Width(compact ? 42f : 46f)))
						_searchText = string.Empty;
				}

				using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
				{
					EditorGUI.BeginChangeCheck();
					bool hideReadOnly = EditorGUILayout.ToggleLeft(new GUIContent("Hide Read-Only Scenes", "Hide scenes with read-only file attribute"), _hideReadOnlyScenes);
					bool rememberStartup = EditorGUILayout.ToggleLeft(new GUIContent("Remember Startup Scene", "Return to startup scene after Play from Scene"), _rememberStartupScene);
					bool setActiveAfterAdditive = EditorGUILayout.ToggleLeft(new GUIContent("Set Active After Additive", "Set newly loaded additive scene as active"), _setActiveAfterAdditive);
					if (EditorGUI.EndChangeCheck())
					{
						_hideReadOnlyScenes = hideReadOnly;
						_rememberStartupScene = rememberStartup;
						_setActiveAfterAdditive = setActiveAfterAdditive;
						SavePreferences();
						MarkDirty();
					}
				}
			}
		}

		private static void DrawSceneList(bool compact)
		{
			bool isLockedInPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

			IEnumerable<SceneEntry> filteredScenes = SceneCache;
			if (!string.IsNullOrEmpty(_searchText))
				filteredScenes = filteredScenes.Where(scene => ContainsIgnoreCase(scene.SceneName, _searchText));

			List<SceneEntry> sceneList = filteredScenes.ToList();
			HashSet<string> recentLookup = new HashSet<string>(RecentScenes, StringComparer.OrdinalIgnoreCase);
			sceneList = sceneList
				.OrderByDescending(scene => scene.IsFavorite)
				.ThenByDescending(scene => recentLookup.Contains(scene.FilePath))
				.ThenBy(scene => scene.SceneName, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (sceneList.Count == 0)
			{
				EditorGUILayout.HelpBox("No scenes found for the selected source.", MessageType.Info);
				return;
			}

			float rowHeight = compact ? 22f : 24f;
			float contentHeight = Mathf.Max(60f, (sceneList.Count * rowHeight) + 6f);
			float maxHeight = _window != null ? Mathf.Max(90f, _window.position.height - (compact ? 90f : 132f)) : 240f;
			bool useScroll = contentHeight > maxHeight;

			if (useScroll)
				_scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(maxHeight));

			foreach (SceneEntry scene in sceneList)
			{
				using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
				{
					bool wasFavorite = scene.IsFavorite;
					bool isFavorite = GUILayout.Toggle(wasFavorite, wasFavorite ? "★" : "☆", EditorStyles.miniButton, GUILayout.Width(22f));
					if (isFavorite != wasFavorite)
						SetFavorite(scene.FilePath, isFavorite);

					string status = GetSceneStatus(scene.FilePath);
					bool isRecent = recentLookup.Contains(scene.FilePath);
					string sceneDisplayName = scene.IsMissing ? $"{scene.SceneName} (Deleted)" : scene.SceneName;
					string recentPrefix = isRecent ? "[Recent] " : string.Empty;
					string label = string.IsNullOrEmpty(status)
						? $"{recentPrefix}{sceneDisplayName}"
						: $"[{status}] {recentPrefix}{sceneDisplayName}";
					bool isReadOnlyScene = scene.IsReadOnly && !scene.IsMissing;

					GUILayout.Label(new GUIContent(label, scene.FilePath), EditorStyles.label);

					if (isReadOnlyScene)
						GUILayout.Label(new GUIContent(_readOnlyContent.image, "Read-only scene"), GUILayout.Width(18f), GUILayout.Height(16f));

					GUILayout.FlexibleSpace();

					if (_sceneSource == SceneSource.AllProject && scene.IsInBuildSettings)
					{
						GUIContent buildStateLabel = new GUIContent(scene.IsEnabledInBuildSettings ? "Build" : "Build (Off)");
						GUILayout.Label(buildStateLabel, EditorStyles.miniLabel, GUILayout.Width(62f));
					}

					if (GUILayout.Button("Ping", GUILayout.Width(42f)))
						PingSceneAsset(scene.FilePath);

					using (new EditorGUI.DisabledScope(isLockedInPlayMode || scene.IsMissing || isReadOnlyScene))
					{
						if (GUILayout.Button(compact ? new GUIContent(_playSceneContent.image, "Play from this scene") : new GUIContent(_playSceneContent.image, " Play"), GUILayout.Width(compact ? 24f : 46f)))
							PlayFromScene(scene.FilePath);

						if (GUILayout.Button("Load", GUILayout.Width(44f)))
							OpenScene(scene.FilePath, OpenSceneMode.Single);

						if (GUILayout.Button(compact ? "+" : "Add", GUILayout.Width(compact ? 24f : 40f)))
							OpenScene(scene.FilePath, OpenSceneMode.Additive);

						if (!compact && GUILayout.Button("Delete", GUILayout.Width(55f)))
							DeleteScene(scene.FilePath);
					}

					if (scene.IsMissing && scene.IsInBuildSettings && GUILayout.Button("Fix", GUILayout.Width(38f)))
						RemoveSceneFromBuildSettings(scene.FilePath);
				}
			}

			if (useScroll)
				GUILayout.EndScrollView();
		}

		private static void DrawFooterBranding()
		{
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				GUIStyle companyStyle = new GUIStyle(EditorStyles.boldLabel)
				{
					alignment = TextAnchor.MiddleCenter
				};

				GUIStyle hintStyle = new GUIStyle(EditorStyles.miniLabel)
				{
					alignment = TextAnchor.MiddleCenter,
					wordWrap = true
				};

				GUILayout.Label(CompanyName, companyStyle);
				GUILayout.Label("Professional tools by Fly Studios Games", hintStyle);

				using (new EditorGUILayout.HorizontalScope())
				{
					GUILayout.FlexibleSpace();
					if (GUILayout.Button("Visit Our Asset Store", GUILayout.Height(22f), GUILayout.MaxWidth(220f)))
						Application.OpenURL(AssetStoreStoreUrl);
					GUILayout.FlexibleSpace();
				}
			}
		}

		#endregion

		#region Scene Operations
		private static void OpenScene(string path, OpenSceneMode mode)
		{
			if (string.IsNullOrEmpty(path))
				return;

			if (IsSceneReadOnly(path))
			{
				if (_window != null)
					_window.ShowNotification(new GUIContent("Scene is read-only and cannot be opened."));
				return;
			}

			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				if (_window != null)
					_window.ShowNotification(new GUIContent("Cannot load scenes during Play Mode."));
				return;
			}

			if (mode == OpenSceneMode.Single && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
				return;

			EditorSceneManager.OpenScene(path, mode);
			RegisterRecent(path);

			if (mode == OpenSceneMode.Additive && _setActiveAfterAdditive)
			{
				Scene loadedScene = SceneManager.GetSceneByPath(path);
				if (loadedScene.IsValid() && loadedScene.isLoaded)
					SceneManager.SetActiveScene(loadedScene);
			}
		}

		private static void PlayFromScene(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;

			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				if (_window != null)
					_window.ShowNotification(new GUIContent("Already in Play Mode."));
				return;
			}

			if (IsSceneReadOnly(path))
			{
				if (_window != null)
					_window.ShowNotification(new GUIContent("Scene is read-only and cannot be opened."));
				return;
			}

			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
			{
				if (_window != null)
					_window.ShowNotification(new GUIContent("Cannot play missing scene (Deleted)."));
				return;
			}

			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
				return;

			if (_rememberStartupScene)
			{
				_startupScenePath = SceneManager.GetActiveScene().path;
				_pendingStartupRestore = !string.IsNullOrEmpty(_startupScenePath)
					&& !string.Equals(_startupScenePath, path, StringComparison.OrdinalIgnoreCase);
				SavePreferences();
			}

			EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
			RegisterRecent(path);
			EditorApplication.delayCall += () =>
			{
				if (!EditorApplication.isPlayingOrWillChangePlaymode)
					EditorApplication.isPlaying = true;
			};
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			if (state != PlayModeStateChange.EnteredEditMode)
				return;

			if (!_rememberStartupScene || !_pendingStartupRestore || string.IsNullOrEmpty(_startupScenePath))
				return;

			if (string.Equals(SceneManager.GetActiveScene().path, _startupScenePath, StringComparison.OrdinalIgnoreCase))
			{
				_pendingStartupRestore = false;
				SavePreferences();
				return;
			}

			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(_startupScenePath) == null)
			{
				_pendingStartupRestore = false;
				SavePreferences();
				return;
			}

			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
				return;

			EditorSceneManager.OpenScene(_startupScenePath, OpenSceneMode.Single);
			RegisterRecent(_startupScenePath);
			_pendingStartupRestore = false;
			SavePreferences();
		}

		private static void DeleteScene(string path)
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				if (_window != null)
					_window.ShowNotification(new GUIContent("Cannot delete scenes during Play Mode."));
				return;
			}

			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
			{
				if (EditorUtility.DisplayDialog("Scene Missing", $"This scene file is missing:\n{path}\n\nRemove stale entry from Build Settings?", "Remove", "Cancel"))
					RemoveSceneFromBuildSettings(path);

				return;
			}

			if (!EditorUtility.DisplayDialog("Delete Scene?", $"Are you sure you want to delete this scene?\n\nFile: {path}", "Yes", "No"))
				return;

			if (!AssetDatabase.DeleteAsset(path))
				EditorUtility.DisplayDialog("Delete Failed", $"Could not delete scene:\n{path}", "OK");

			MarkDirty();
		}

		private static void PingSceneAsset(string path)
		{
			SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
			if (sceneAsset != null)
				EditorGUIUtility.PingObject(sceneAsset);
		}

		private static void RemoveSceneFromBuildSettings(string path)
		{
			EditorBuildSettingsScene[] currentScenes = EditorBuildSettings.scenes;
			EditorBuildSettingsScene[] newScenes = currentScenes
				.Where(scene => !string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase))
				.ToArray();

			if (newScenes.Length == currentScenes.Length)
			{
				if (_window != null)
					_window.ShowNotification(new GUIContent("Scene is not in Build Settings."));
				return;
			}

			EditorBuildSettings.scenes = newScenes;
			if (_window != null)
				_window.ShowNotification(new GUIContent("Removed missing scene from Build Settings."));

			MarkDirty();
		}
		#endregion

		#region Data
		private static void RefreshSceneCacheIfNeeded(bool force = false)
		{
			if (!force && !_isDirty)
				return;

			SceneCache.Clear();
			SceneCache.AddRange(CollectScenes());
			_isDirty = false;
		}

		private static IEnumerable<SceneEntry> CollectScenes()
		{
			Dictionary<string, EditorBuildSettingsScene> buildLookup = EditorBuildSettings.scenes
				.ToDictionary(scene => scene.path, scene => scene, StringComparer.OrdinalIgnoreCase);

			IEnumerable<string> scenePaths = _sceneSource == SceneSource.BuildSettings
				? EditorBuildSettings.scenes.Select(scene => scene.path)
				: AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath);

			foreach (string path in scenePaths.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (string.IsNullOrEmpty(path))
					continue;

				bool isMissing = AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null;
				bool isReadOnly = !isMissing && IsSceneReadOnly(path);
				if (_hideReadOnlyScenes && isReadOnly)
					continue;

				bool foundInBuild = buildLookup.TryGetValue(path, out EditorBuildSettingsScene buildScene);
				yield return new SceneEntry
				{
					FilePath = path,
					SceneName = Path.GetFileNameWithoutExtension(path),
					IsInBuildSettings = foundInBuild,
					IsEnabledInBuildSettings = foundInBuild && buildScene.enabled,
					IsMissing = isMissing,
					IsReadOnly = isReadOnly,
					IsFavorite = FavoriteScenes.Contains(path)
				};
			}
		}

		private static string GetSceneStatus(string path)
		{
			Scene activeScene = SceneManager.GetActiveScene();
			if (string.Equals(activeScene.path, path, StringComparison.OrdinalIgnoreCase))
				return "ACTIVE";

			for (int index = 0; index < SceneManager.sceneCount; index++)
			{
				Scene loadedScene = SceneManager.GetSceneAt(index);
				if (string.Equals(loadedScene.path, path, StringComparison.OrdinalIgnoreCase))
					return "LOADED";
			}

			return string.Empty;
		}
		#endregion

		#region Helpers
		private static bool ContainsIgnoreCase(string source, string value)
		{
			return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static void EnsureGuiContentInitialized()
		{
			if (_playSceneContent == null)
				_playSceneContent = EditorGUIUtility.IconContent("PlayButton");

			if (_readOnlyContent == null)
				_readOnlyContent = EditorGUIUtility.IconContent("LockIcon-On");
		}

		private static bool IsSceneReadOnly(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath))
				return false;

			try
			{
				bool isLockedByProvider = !AssetDatabase.IsOpenForEdit(assetPath, StatusQueryOptions.UseCachedIfPossible);

				string projectRoot = Directory.GetParent(Application.dataPath).FullName;
				string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));

				if (!File.Exists(absolutePath))
					return isLockedByProvider;

				FileAttributes attributes = File.GetAttributes(absolutePath);
				bool hasReadOnlyAttribute = (attributes & FileAttributes.ReadOnly) != 0;
				return isLockedByProvider || hasReadOnlyAttribute;
			}
			catch
			{
				return false;
			}
		}

		private static void SyncHideReadOnlyPreference()
		{
			bool prefValue = EditorPrefs.GetBool(HideReadOnlyPrefKey, false);
			if (prefValue == _hideReadOnlyScenes)
				return;

			_hideReadOnlyScenes = prefValue;
			MarkDirty();
		}

		private static void SetFavorite(string path, bool isFavorite)
		{
			if (string.IsNullOrEmpty(path))
				return;

			if (isFavorite)
				FavoriteScenes.Add(path);
			else
				FavoriteScenes.Remove(path);

			SavePreferences();
			MarkDirty();
		}

		private static void RegisterRecent(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;

			RecentScenes.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
			RecentScenes.Insert(0, path);

			if (RecentScenes.Count > MaxRecentScenes)
				RecentScenes.RemoveRange(MaxRecentScenes, RecentScenes.Count - MaxRecentScenes);

			SavePreferences();
			MarkDirty();
		}

		private static void LoadPathCollection(string value, ICollection<string> target)
		{
			target.Clear();
			if (string.IsNullOrEmpty(value))
				return;

			string[] parts = value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
			for (int index = 0; index < parts.Length; index++)
			{
				string path = parts[index].Trim();
				if (!string.IsNullOrEmpty(path))
					target.Add(path);
			}
		}

		private static string SerializePathCollection(IEnumerable<string> values)
		{
			return string.Join("|", values.Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.OrdinalIgnoreCase));
		}

		private static void MarkDirty()
		{
			_isDirty = true;
			if (_window != null)
				_window.Repaint();
		}

		private static void SavePreferences()
		{
			EditorPrefs.SetInt(SourcePrefKey, (int)_sceneSource);
			EditorPrefs.SetBool(HideReadOnlyPrefKey, _hideReadOnlyScenes);
			EditorPrefs.SetBool(RememberStartupScenePrefKey, _rememberStartupScene);
			EditorPrefs.SetBool(SetActiveAfterAdditivePrefKey, _setActiveAfterAdditive);
			EditorPrefs.SetString(StartupScenePathPrefKey, _startupScenePath ?? string.Empty);
			EditorPrefs.SetBool(PendingStartupRestorePrefKey, _pendingStartupRestore);
			EditorPrefs.SetString(FavoriteScenesPrefKey, SerializePathCollection(FavoriteScenes));
			EditorPrefs.SetString(RecentScenesPrefKey, SerializePathCollection(RecentScenes));
		}

		private static void LoadPreferences()
		{
			int value = EditorPrefs.GetInt(SourcePrefKey, 0);
			if (!Enum.IsDefined(typeof(SceneSource), value))
				value = 0;

			_sceneSource = (SceneSource)value;
			_hideReadOnlyScenes = EditorPrefs.GetBool(HideReadOnlyPrefKey, false);
			_rememberStartupScene = EditorPrefs.GetBool(RememberStartupScenePrefKey, true);
			_setActiveAfterAdditive = EditorPrefs.GetBool(SetActiveAfterAdditivePrefKey, true);
			_startupScenePath = EditorPrefs.GetString(StartupScenePathPrefKey, string.Empty);
			_pendingStartupRestore = EditorPrefs.GetBool(PendingStartupRestorePrefKey, false);

			LoadPathCollection(EditorPrefs.GetString(FavoriteScenesPrefKey, string.Empty), FavoriteScenes);
			RecentScenes.Clear();
			LoadPathCollection(EditorPrefs.GetString(RecentScenesPrefKey, string.Empty), RecentScenes);
		}
		#endregion
	}
}
