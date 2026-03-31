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
		private const string CompanyName = "Fly Studios Games";
		private const string AssetStoreStoreUrl = "https://assetstore.unity.com/publishers/56140";
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
		private static EasySceneSwitcherWindow _window;
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
		}
		#endregion

		#region Menu
		[MenuItem("Tools/Easy Scene Switcher Pro/Open Window", priority = 10)]
		[MenuItem("Window/Easy Scene Switcher Pro/Open Window", priority = 10)]
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

			RefreshSceneCacheIfNeeded();
			bool isLockedInPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				DrawHeader();
				DrawToolbar();

				if (isLockedInPlayMode)
					EditorGUILayout.HelpBox("In Play Mode, scene switching and delete are disabled. You can still ping scene assets.", MessageType.Warning);

				DrawSceneList(compact: false);
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
				GUILayout.FlexibleSpace();
				GUILayout.Label($"Scenes: {SceneCache.Count}", EditorStyles.miniLabel);
			}

			EditorGUILayout.Space(2f);
		}

		private static void DrawToolbar()
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

					if (GUILayout.Button("Refresh", GUILayout.Width(74f)))
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
		}

		private static void DrawSceneList(bool compact)
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
					string status = GetSceneStatus(scene.FilePath);
					string sceneDisplayName = scene.IsMissing ? $"{scene.SceneName} (Deleted)" : scene.SceneName;
					string label = string.IsNullOrEmpty(status) ? sceneDisplayName : $"[{status}] {sceneDisplayName}";

					GUILayout.Label(new GUIContent(label, scene.FilePath), EditorStyles.label);
					GUILayout.FlexibleSpace();

					if (_sceneSource == SceneSource.AllProject && scene.IsInBuildSettings)
					{
						GUIContent buildStateLabel = new GUIContent(scene.IsEnabledInBuildSettings ? "Build" : "Build (Off)");
						GUILayout.Label(buildStateLabel, EditorStyles.miniLabel, GUILayout.Width(62f));
					}

					if (GUILayout.Button("Ping", GUILayout.Width(42f)))
						PingSceneAsset(scene.FilePath);

					using (new EditorGUI.DisabledScope(isLockedInPlayMode || scene.IsMissing))
					{
						if (GUILayout.Button("Load", GUILayout.Width(44f)))
							OpenScene(scene.FilePath);

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
		private static void OpenScene(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;

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
				bool foundInBuild = buildLookup.TryGetValue(path, out EditorBuildSettingsScene buildScene);
				yield return new SceneEntry
				{
					FilePath = path,
					SceneName = Path.GetFileNameWithoutExtension(path),
					IsInBuildSettings = foundInBuild,
					IsEnabledInBuildSettings = foundInBuild && buildScene.enabled,
					IsMissing = isMissing
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

		private static void MarkDirty()
		{
			_isDirty = true;
			if (_window != null)
				_window.Repaint();
		}

		private static void SavePreferences()
		{
			EditorPrefs.SetInt(SourcePrefKey, (int)_sceneSource);
		}

		private static void LoadPreferences()
		{
			int value = EditorPrefs.GetInt(SourcePrefKey, 0);
			if (!Enum.IsDefined(typeof(SceneSource), value))
				value = 0;

			_sceneSource = (SceneSource)value;
		}
		#endregion
	}
}
