using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
#if UNITY_6000_0_OR_NEWER
using UnityEditor.Toolbars;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FlyStudiosGames.EasySceneSwitcherPro.Editor
{
	/// <summary>
	/// Injects a compact scene switcher directly into the Unity top toolbar, next to Play controls.
	/// </summary>
	[InitializeOnLoad]
	internal static class EasySceneSwitcherToolbar
	{
		#region Constants
		private const string SourcePrefKey = "FlyStudiosGames.EasySceneSwitcherPro.Toolbar.SceneSource";
		private const string ToolbarEnabledPrefKey = "FlyStudiosGames.EasySceneSwitcherPro.Toolbar.Enabled";
		private const string RootFieldName = "m_Root";
		private const string ContainerName = "EasySceneSwitcherToolbarContainer";
		private const float DefaultVerticalOffset = 1f;
#if UNITY_6000_0_OR_NEWER
		private const string SourceElementId = "EasySceneSwitcherPro/Toolbar/Source";
		private const string SceneElementId = "EasySceneSwitcherPro/Toolbar/Scene";
#endif

		private const float SourceWidth = 52f;
		private const float MinScenePopupWidth = 90f;
		private const float MaxScenePopupWidth = 150f;
		private const float LockedLabelWidth = 46f;

		private static readonly string[] PlayZoneNames =
		{
			"ToolbarZonePlayModes",
			"ToolbarZonePlayMode",
			"ToolbarZonePlay"
		};
		#endregion

		#region GUI Content
		private static readonly GUIContent SourceBuildContent = new GUIContent("Build", "List scenes from Build Settings");
		private static readonly GUIContent SourceAllContent = new GUIContent("All", "List all scenes in the project");
		private static readonly GUIContent NoScenesContent = new GUIContent("No Scenes", "No scenes found for selected source");
		private static readonly GUIContent LockedContent = new GUIContent("Locked", "Scene switching is disabled in Play Mode");
		#endregion

		#region State
		private static readonly Type ToolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
		private static readonly List<SceneItem> SceneCache = new List<SceneItem>();

		private static ScriptableObject _toolbar;
		private static VisualElement _toolbarRoot;
		private static VisualElement _playHostZone;
		private static IMGUIContainer _toolbarContainer;
		private static bool _isDirty = true;
		private static bool _isToolbarEnabled = true;
		private static SceneSource _sceneSource;
		private static float _scenePopupWidth = 110f;
		#endregion

		#region Menu
		[MenuItem("Tools/Easy Scene Switcher Pro/Settings/Toolbar Scene Switcher/On", true)]
		private static bool ValidateEnableToolbarSceneSwitcher()
		{
			return !_isToolbarEnabled;
		}

		[MenuItem("Tools/Easy Scene Switcher Pro/Settings/Toolbar Scene Switcher/On", priority = 100)]
		private static void EnableToolbarSceneSwitcher()
		{
			_isToolbarEnabled = true;
			SavePreferences();
			MarkDirty();
			#if UNITY_6000_0_OR_NEWER
			RefreshToolbarElements();
			#else
			EnsureToolbarInjected();
			UpdateOverlayPosition();
			#endif
		}

		[MenuItem("Tools/Easy Scene Switcher Pro/Settings/Toolbar Scene Switcher/Off", true)]
		private static bool ValidateDisableToolbarSceneSwitcher()
		{
			return _isToolbarEnabled;
		}

		[MenuItem("Tools/Easy Scene Switcher Pro/Settings/Toolbar Scene Switcher/Off", priority = 101)]
		private static void DisableToolbarSceneSwitcher()
		{
			_isToolbarEnabled = false;
			SavePreferences();
			#if UNITY_6000_0_OR_NEWER
			RefreshToolbarElements();
			#else
			RemoveInjectedToolbarContainer();
			#endif
		}
		#endregion

		#region Types
		private enum SceneSource
		{
			BuildSettings = 0,
			AllProject = 1
		}

		private sealed class SceneItem
		{
			public string Name;
			public string Path;
			public bool IsMissing;
		}
		#endregion

		#region Bootstrap
		static EasySceneSwitcherToolbar()
		{
			LoadPreferences();

			#if UNITY_6000_0_OR_NEWER
			EditorApplication.projectChanged += OnProjectChanged;
			EditorBuildSettings.sceneListChanged += OnBuildSettingsSceneListChanged;
			EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
			RefreshToolbarElements();
			#else
			EditorApplication.update += OnEditorUpdate;
			EditorApplication.projectChanged += MarkDirty;
			EditorBuildSettings.sceneListChanged += MarkDirty;
			EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
			#endif
		}

		#if !UNITY_6000_0_OR_NEWER
		private static void OnEditorUpdate()
		{
			if (!_isToolbarEnabled)
			{
				RemoveInjectedToolbarContainer();
				return;
			}

			EnsureToolbarInjected();
			UpdateOverlayPosition();
		}
		#endif

		private static void OnActiveSceneChanged(Scene oldScene, Scene newScene)
		{
			#if UNITY_6000_0_OR_NEWER
			RefreshToolbarElements();
			#else
			if (_toolbarContainer != null)
				_toolbarContainer.MarkDirtyRepaint();
			#endif
		}

		#if UNITY_6000_0_OR_NEWER
		private static void OnProjectChanged()
		{
			MarkDirty();
			RefreshToolbarElements();
		}

		private static void OnBuildSettingsSceneListChanged()
		{
			MarkDirty();
			RefreshToolbarElements();
		}
		#endif
		#endregion

		#region Unity 6 Toolbar Elements
		#if UNITY_6000_0_OR_NEWER
		[MainToolbarElement(SourceElementId, defaultDockPosition = MainToolbarDockPosition.Middle)]
		public static MainToolbarElement CreateSourceElement()
		{
			GUIContent content = _sceneSource == SceneSource.BuildSettings ? SourceBuildContent : SourceAllContent;
			return new MainToolbarButton(new MainToolbarContent(content.text, content.tooltip), ShowSourceMenu);
		}

		[MainToolbarElement(SceneElementId, defaultDockPosition = MainToolbarDockPosition.Middle)]
		public static MainToolbarElement CreateSceneElement()
		{
			RefreshSceneCacheIfNeeded();

			string label = GetCurrentSceneToolbarLabel();
			string tooltip = EditorApplication.isPlayingOrWillChangePlaymode
				? "Scene switching is disabled in Play Mode"
				: "Select scene";

			return new MainToolbarButton(new MainToolbarContent(label, tooltip), ShowSceneMenu);
		}

		private static void ShowSourceMenu()
		{
			GenericMenu menu = new GenericMenu();
			menu.AddItem(new GUIContent("Build Settings"), _sceneSource == SceneSource.BuildSettings, () => SetSceneSource(SceneSource.BuildSettings));
			menu.AddItem(new GUIContent("All Project"), _sceneSource == SceneSource.AllProject, () => SetSceneSource(SceneSource.AllProject));
			menu.ShowAsContext();
		}

		private static void ShowSceneMenu()
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				ShowToolbarNotification("Scene switching is disabled in Play Mode.");
				return;
			}

			RefreshSceneCacheIfNeeded();
			if (SceneCache.Count == 0)
			{
				ShowToolbarNotification("No scenes found for selected source.");
				return;
			}

			int currentIndex = GetActiveSceneIndex();
			GenericMenu menu = new GenericMenu();

			for (int index = 0; index < SceneCache.Count; index++)
			{
				SceneItem scene = SceneCache[index];
				string title = scene.IsMissing ? $"{scene.Name} (Deleted)" : scene.Name;
				bool isCurrent = index == currentIndex;

				if (scene.IsMissing)
				{
					menu.AddDisabledItem(new GUIContent(title), isCurrent);
					continue;
				}

				string path = scene.Path;
				menu.AddItem(new GUIContent(title), isCurrent, () => OpenScene(path));
			}

			menu.ShowAsContext();
		}

		private static string GetCurrentSceneToolbarLabel()
		{
			if (SceneCache.Count == 0)
				return NoScenesContent.text;

			int currentIndex = GetActiveSceneIndex();
			if (currentIndex < 0 || currentIndex >= SceneCache.Count)
				return "Scene";

			SceneItem current = SceneCache[currentIndex];
			return current.IsMissing ? $"{current.Name} (Deleted)" : current.Name;
		}

		private static void RefreshToolbarElements()
		{
			MainToolbar.Refresh(SourceElementId);
			MainToolbar.Refresh(SceneElementId);
		}
		#endif
		#endregion

		#region Injection
		#if !UNITY_6000_0_OR_NEWER
		private static void EnsureToolbarInjected()
		{
			if (ToolbarType == null)
				return;

			UnityEngine.Object[] toolbars = Resources.FindObjectsOfTypeAll(ToolbarType);
			if (toolbars == null || toolbars.Length == 0)
				return;

			ScriptableObject currentToolbar = toolbars[0] as ScriptableObject;
			if (currentToolbar == null)
				return;

			bool hasValidContainer = _toolbarContainer != null
				&& _toolbarRoot != null
				&& _toolbarContainer.parent == _toolbarRoot;

			bool hasValidPlayZone = _playHostZone != null && _playHostZone.panel != null;

			if (_toolbar == currentToolbar && hasValidContainer && hasValidPlayZone)
				return;

			_toolbar = currentToolbar;
			InjectIntoToolbar();
		}

		private static void InjectIntoToolbar()
		{
			FieldInfo rootField = ToolbarType.GetField(RootFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
			if (rootField == null)
				return;

			VisualElement root = rootField.GetValue(_toolbar) as VisualElement;
			if (root == null)
				return;

			VisualElement playZone = FindPlayHostZone(root);
			if (playZone == null)
				return;

			_toolbarRoot = root;
			_playHostZone = playZone;

			VisualElement existing = root.Q(ContainerName);
			if (existing != null)
				existing.RemoveFromHierarchy();

			_toolbarContainer = new IMGUIContainer(DrawToolbarGui)
			{
				name = ContainerName
			};

			_toolbarContainer.style.marginLeft = 1;
			_toolbarContainer.style.marginRight = 0;
			_toolbarContainer.style.width = 190;
			_toolbarContainer.style.flexShrink = 0;
			_toolbarContainer.style.position = Position.Absolute;
			_toolbarContainer.style.top = 0;
			_toolbarContainer.style.height = 22;
			_toolbarContainer.style.unityTextAlign = TextAnchor.MiddleLeft;

			root.Add(_toolbarContainer);
			UpdateOverlayPosition();
		}

		private static void RemoveInjectedToolbarContainer()
		{
			if (_toolbarRoot != null)
			{
				VisualElement existing = _toolbarRoot.Q(ContainerName);
				if (existing != null)
					existing.RemoveFromHierarchy();
			}

			_toolbarContainer = null;
		}

		private static VisualElement FindPlayHostZone(VisualElement root)
		{
			for (int index = 0; index < PlayZoneNames.Length; index++)
			{
				VisualElement zone = root.Q(PlayZoneNames[index]);
				if (zone != null)
					return zone;
			}

			List<VisualElement> allElements = root.Query<VisualElement>().ToList();
			for (int index = 0; index < allElements.Count; index++)
			{
				string name = allElements[index].name;
				if (!string.IsNullOrEmpty(name)
					&& name.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0
					&& name.IndexOf("ToolbarZone", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return allElements[index];
				}
			}

			return root.Q("ToolbarZoneLeftAlign");
		}

		private static void UpdateOverlayPosition()
		{
			if (_toolbarContainer == null || _toolbarRoot == null || _playHostZone == null)
				return;

			if (_playHostZone.panel == null)
			{
				_playHostZone = FindPlayHostZone(_toolbarRoot);
				if (_playHostZone == null)
					return;
			}

			Rect playWorldRect = _playHostZone.worldBound;
			if (playWorldRect.width <= 0f)
				return;

			Vector2 playLocalTopLeft = _toolbarRoot.WorldToLocal(new Vector2(playWorldRect.xMin, playWorldRect.yMin));

			float targetLeft = playLocalTopLeft.x - _toolbarContainer.resolvedStyle.width - 4f;
			if (targetLeft < 0f)
				targetLeft = 0f;

			float containerHeight = _toolbarContainer.resolvedStyle.height;
			if (containerHeight <= 0f)
				containerHeight = 22f;

			float centeredTop = playLocalTopLeft.y + ((playWorldRect.height - containerHeight) * 0.5f);

			_toolbarContainer.style.left = targetLeft;
			_toolbarContainer.style.top = centeredTop + DefaultVerticalOffset;
		}
		#endif
		#endregion

		#region GUI
		private static void DrawToolbarGui()
		{
			RefreshSceneCacheIfNeeded();

			bool isLocked = EditorApplication.isPlayingOrWillChangePlaymode;
			UpdateCompactWidths(isLocked);

			using (new EditorGUI.DisabledScope(isLocked))
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					DrawSourceDropdown();
					DrawSceneDropdown();
				}
			}

			if (isLocked)
				GUILayout.Label(LockedContent, EditorStyles.miniLabel, GUILayout.Width(LockedLabelWidth));
		}

		private static void DrawSourceDropdown()
		{
			GUIContent content = _sceneSource == SceneSource.BuildSettings ? SourceBuildContent : SourceAllContent;
			if (!EditorGUILayout.DropdownButton(content, FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(SourceWidth)))
				return;

			GenericMenu menu = new GenericMenu();
			menu.AddItem(new GUIContent("Build Settings"), _sceneSource == SceneSource.BuildSettings, () => SetSceneSource(SceneSource.BuildSettings));
			menu.AddItem(new GUIContent("All Project"), _sceneSource == SceneSource.AllProject, () => SetSceneSource(SceneSource.AllProject));
			menu.ShowAsContext();
		}

		private static void DrawSceneDropdown()
		{
			if (SceneCache.Count == 0)
			{
				GUILayout.Label(NoScenesContent, EditorStyles.miniLabel, GUILayout.Width(_scenePopupWidth));
				return;
			}

			int currentIndex = GetActiveSceneIndex();
			string[] sceneNames = SceneCache
				.Select(scene => scene.IsMissing ? $"{scene.Name} (Deleted)" : scene.Name)
				.ToArray();
			int shownIndex = Mathf.Clamp(currentIndex, 0, sceneNames.Length - 1);

			EditorGUI.BeginChangeCheck();
			int newIndex = EditorGUILayout.Popup(shownIndex, sceneNames, EditorStyles.toolbarPopup, GUILayout.Width(_scenePopupWidth));
			if (!EditorGUI.EndChangeCheck())
				return;

			if (newIndex == currentIndex)
				return;

			if (SceneCache[newIndex].IsMissing)
			{
				ShowToolbarNotification("Selected scene is missing (Deleted). Remove it from Build Settings.");
				return;
			}

			OpenScene(SceneCache[newIndex].Path);
		}

		private static void UpdateCompactWidths(bool isLocked)
		{
			_scenePopupWidth = CalculateScenePopupWidth();

			float width = SourceWidth + _scenePopupWidth + 6f;
			if (isLocked)
				width += LockedLabelWidth;

			if (_toolbarContainer != null)
				_toolbarContainer.style.width = width;
		}

		private static float CalculateScenePopupWidth()
		{
			if (SceneCache.Count == 0)
			{
				float width = EditorStyles.miniLabel.CalcSize(NoScenesContent).x + 8f;
				return Mathf.Clamp(width, MinScenePopupWidth, MaxScenePopupWidth);
			}

			int currentIndex = GetActiveSceneIndex();
			int shownIndex = Mathf.Clamp(currentIndex, 0, SceneCache.Count - 1);
			string title = SceneCache[shownIndex].Name;
			float widthFromText = EditorStyles.toolbarPopup.CalcSize(new GUIContent(title)).x + 24f;
			return Mathf.Clamp(widthFromText, MinScenePopupWidth, MaxScenePopupWidth);
		}
		#endregion

		#region Scene Operations
		private static void OpenScene(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;

			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
			{
				ShowToolbarNotification("Cannot load missing scene (Deleted).");
				return;
			}

			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
				return;

			EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
		}

		private static int GetActiveSceneIndex()
		{
			string activePath = SceneManager.GetActiveScene().path;
			for (int index = 0; index < SceneCache.Count; index++)
			{
				if (string.Equals(SceneCache[index].Path, activePath, StringComparison.OrdinalIgnoreCase))
					return index;
			}

			return -1;
		}
		#endregion

		#region Data
		private static void RefreshSceneCacheIfNeeded()
		{
			if (!_isDirty)
				return;

			SceneCache.Clear();

			IEnumerable<string> scenePaths = _sceneSource == SceneSource.BuildSettings
				? EditorBuildSettings.scenes.Select(scene => scene.path)
				: AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath);

			foreach (string scenePath in scenePaths.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (string.IsNullOrEmpty(scenePath))
					continue;

				SceneCache.Add(new SceneItem
				{
					Path = scenePath,
					Name = System.IO.Path.GetFileNameWithoutExtension(scenePath),
					IsMissing = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null
				});
			}

			SceneCache.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			_isDirty = false;

			if (_toolbarContainer != null)
				_toolbarContainer.MarkDirtyRepaint();
		}

		private static void SetSceneSource(SceneSource source)
		{
			if (_sceneSource == source)
				return;

			_sceneSource = source;
			SavePreferences();
			MarkDirty();
		}
		#endregion

		#region Helpers
		private static void MarkDirty()
		{
			_isDirty = true;
			#if UNITY_6000_0_OR_NEWER
			RefreshToolbarElements();
			#else
			if (_toolbarContainer != null)
				_toolbarContainer.MarkDirtyRepaint();
			#endif
		}

		private static void ShowToolbarNotification(string message)
		{
			if (_toolbar == null)
				return;

			EditorWindow toolbarWindow = _toolbar as EditorWindow;
			if (toolbarWindow != null)
				toolbarWindow.ShowNotification(new GUIContent(message));
		}

		private static void LoadPreferences()
		{
			int value = EditorPrefs.GetInt(SourcePrefKey, 0);
			if (!Enum.IsDefined(typeof(SceneSource), value))
				value = 0;

			_sceneSource = (SceneSource)value;
			_isToolbarEnabled = EditorPrefs.GetBool(ToolbarEnabledPrefKey, true);
		}

		private static void SavePreferences()
		{
			EditorPrefs.SetInt(SourcePrefKey, (int)_sceneSource);
			EditorPrefs.SetBool(ToolbarEnabledPrefKey, _isToolbarEnabled);
		}
		#endregion
	}
}
