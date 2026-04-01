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
		private const string AssetStoreStoreUrl = "https://assetstore.unity.com/publishers/56140";
		private const float NarrowWidth = 430f;
		private const float VeryNarrowWidth = 365f;
		private const float HeaderHeight = 22f;
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
		private static EasySceneSwitcherWindow _window;
		private static GUIContent _playSceneContent;
		private static GUIContent _readOnlyContent;
		private static GUIStyle _titleStyle;
		private static GUIStyle _metaStyle;
		private static GUIStyle _moreToolsLinkStyle;
		private static GUIStyle _sceneNameStyle;
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
		}

		private void OnDisable()
		{
			if (_window == this)
				_window = null;

			EditorBuildSettings.sceneListChanged -= MarkDirty;
			EditorApplication.projectChanged -= MarkDirty;
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
			float width = _window != null ? _window.position.width : 360f;
			bool compact = width < NarrowWidth;
			bool veryNarrow = width < VeryNarrowWidth;

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				DrawHeader(compact, veryNarrow);
				DrawToolbar(compact, veryNarrow);

				if (isLockedInPlayMode)
					EditorGUILayout.HelpBox("In Play Mode, scene switching and delete are disabled. You can still ping scene assets.", MessageType.Warning);

				DrawSceneList(compact, veryNarrow);
			}
		}
		#endregion

		#region Drawing
		private static void DrawHeader(bool compact, bool veryNarrow)
		{
			EnsureStylesInitialized();

			using (new EditorGUILayout.HorizontalScope(GUILayout.Height(HeaderHeight)))
			{
				GUILayout.Label("Fly Studios Games: ", _titleStyle, GUILayout.Height(HeaderHeight));
				GUILayout.Space(8f);
				if (GUILayout.Button("More Tools", _moreToolsLinkStyle, GUILayout.Height(HeaderHeight)))
						Application.OpenURL(AssetStoreStoreUrl);
				GUILayout.FlexibleSpace();
				GUILayout.Label($"Scenes: {SceneCache.Count}", EditorStyles.miniLabel, GUILayout.Height(HeaderHeight));
			}

	
			EditorGUILayout.Space(2f);
		}

		private static void DrawToolbar(bool compact, bool veryNarrow)
		{
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				if (veryNarrow)
				{
					EditorGUI.BeginChangeCheck();
					int selectedSource = GUILayout.Toolbar((int)_sceneSource, SourceOptions, EditorStyles.miniButton);
					if (EditorGUI.EndChangeCheck())
					{
						_sceneSource = (SceneSource)Mathf.Clamp(selectedSource, 0, SourceOptions.Length - 1);
						SavePreferences();
						MarkDirty();
					}

					if (GUILayout.Button("Refresh", GUILayout.Height(20f)))
					{
						MarkDirty();
						RefreshSceneCacheIfNeeded(force: true);
					}

					using (new EditorGUILayout.HorizontalScope())
					{
						_searchText = EditorGUILayout.TextField(_searchText);
						if (GUILayout.Button("Clear", GUILayout.Width(52f)))
							_searchText = string.Empty;
					}
				}
				else
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

						if (GUILayout.Button("Refresh", GUILayout.Width(compact ? 62f : 74f)))
						{
							MarkDirty();
							RefreshSceneCacheIfNeeded(force: true);
						}
					}

					using (new EditorGUILayout.HorizontalScope())
					{
						EditorGUILayout.LabelField("Search", GUILayout.Width(52f));
						_searchText = EditorGUILayout.TextField(_searchText);

						if (GUILayout.Button("Clear", GUILayout.Width(46f)))
							_searchText = string.Empty;
					}
				}

				using (new EditorGUILayout.HorizontalScope())
				{
					EditorGUI.BeginChangeCheck();
					bool hideReadOnly = EditorGUILayout.ToggleLeft(new GUIContent("Hide Read-Only Scenes", "Hide scenes with read-only file attribute"), _hideReadOnlyScenes);
					if (EditorGUI.EndChangeCheck())
					{
						_hideReadOnlyScenes = hideReadOnly;
						SavePreferences();
						MarkDirty();
					}
				}
			}
		}

		private static void DrawSceneList(bool compact, bool veryNarrow)
		{
			bool isLockedInPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

			IEnumerable<SceneEntry> filteredScenes = SceneCache;
			if (!string.IsNullOrEmpty(_searchText))
				filteredScenes = filteredScenes.Where(scene => ContainsIgnoreCase(scene.SceneName, _searchText));

			List<SceneEntry> sceneList = filteredScenes.ToList();
			if (sceneList.Count == 0)
			{
				EditorGUILayout.HelpBox("No scenes found for the selected source.", MessageType.Info);
				return;
			}

			float rowHeight = veryNarrow ? 24f : compact ? 23f : 24f;
			float pingWidth = veryNarrow ? 34f : compact ? 38f : 42f;
			float playWidth = veryNarrow ? 20f : compact ? 24f : 44f;
			float loadWidth = veryNarrow ? 36f : 44f;
			float deleteWidth = compact ? 48f : 55f;
			float buildLabelWidth = compact ? 54f : 62f;

			_scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

			foreach (SceneEntry scene in sceneList)
			{
				using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox, GUILayout.Height(rowHeight)))
				{
					string status = GetSceneStatus(scene.FilePath);
					string sceneDisplayName = scene.IsMissing ? $"{scene.SceneName} (Deleted)" : scene.SceneName;
					string label = string.IsNullOrEmpty(status) ? sceneDisplayName : $"[{status}] {sceneDisplayName}";
					bool isReadOnlyScene = scene.IsReadOnly && !scene.IsMissing;

					GUILayout.Label(new GUIContent(label, scene.FilePath), _sceneNameStyle, GUILayout.MinWidth(80f), GUILayout.Height(rowHeight - 2f));

					if (isReadOnlyScene)
						GUILayout.Label(new GUIContent(_readOnlyContent.image, "Read-only scene"), GUILayout.Width(18f), GUILayout.Height(16f));

					GUILayout.FlexibleSpace();

					if (_sceneSource == SceneSource.AllProject && scene.IsInBuildSettings)
					{
						GUIContent buildStateLabel = new GUIContent(scene.IsEnabledInBuildSettings ? "Build" : compact ? "Off" : "Build (Off)");
						GUILayout.Label(buildStateLabel, EditorStyles.miniLabel, GUILayout.Width(buildLabelWidth));
					}

					if (GUILayout.Button(veryNarrow ? "P" : "Ping", GUILayout.Width(pingWidth)))
						PingSceneAsset(scene.FilePath);

					using (new EditorGUI.DisabledScope(isLockedInPlayMode || scene.IsMissing || isReadOnlyScene))
					{
						GUIContent playContent = veryNarrow
							? new GUIContent(_playSceneContent.image, "Play from this scene")
							: compact ? new GUIContent(_playSceneContent.image, "Play from this scene") : new GUIContent(_playSceneContent.image, " Play");

						if (GUILayout.Button(playContent, GUILayout.Width(playWidth)))
							PlayFromScene(scene.FilePath);

						if (GUILayout.Button(veryNarrow ? "L" : "Load", GUILayout.Width(loadWidth)))
							OpenScene(scene.FilePath);

						if (!veryNarrow && GUILayout.Button(compact ? "Del" : "Delete", GUILayout.Width(deleteWidth)))
							DeleteScene(scene.FilePath);
					}

					if (scene.IsMissing && scene.IsInBuildSettings && GUILayout.Button(veryNarrow ? "Fix" : "Fix", GUILayout.Width(veryNarrow ? 30f : 38f)))
						RemoveSceneFromBuildSettings(scene.FilePath);
				}
			}

			GUILayout.EndScrollView();
		}

		#endregion

		#region Scene Operations
		private static void OpenScene(string path)
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

			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
				return;

			EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
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

			EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
			EditorApplication.delayCall += () =>
			{
				if (!EditorApplication.isPlayingOrWillChangePlaymode)
					EditorApplication.isPlaying = true;
			};
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
					IsReadOnly = isReadOnly
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

		private static void EnsureStylesInitialized()
		{
			if (_titleStyle == null)
			{
				_titleStyle = new GUIStyle(EditorStyles.boldLabel)
				{
					alignment = TextAnchor.MiddleLeft,
					fontSize = 12
				};
			}

			if (_metaStyle == null)
			{
				_metaStyle = new GUIStyle(EditorStyles.miniLabel)
				{
					alignment = TextAnchor.MiddleLeft
				};
			}

			if (_sceneNameStyle == null)
			{
				_sceneNameStyle = new GUIStyle(EditorStyles.label)
				{
					alignment = TextAnchor.MiddleLeft
				};
			}

			if (_moreToolsLinkStyle == null)
			{
				_moreToolsLinkStyle = new GUIStyle(EditorStyles.linkLabel)
				{
					alignment = TextAnchor.MiddleLeft
				};
			}
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
		}

		private static void LoadPreferences()
		{
			int value = EditorPrefs.GetInt(SourcePrefKey, 0);
			if (!Enum.IsDefined(typeof(SceneSource), value))
				value = 0;

			_sceneSource = (SceneSource)value;
			_hideReadOnlyScenes = EditorPrefs.GetBool(HideReadOnlyPrefKey, false);
		}
		#endregion
	}
}
