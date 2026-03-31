using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class MainToolbarElementStyler {
    public static void StyleElement<T>(string elementName, System.Action<T> styleAction) where T : VisualElement {
        EditorApplication.delayCall += () => {
            ApplyStyle(elementName, (element) => {
                T targetElement = null;

                if (element is T typedElement) {
                    targetElement = typedElement;
                } else {
                    targetElement = element.Query<T>().First();
                }

                if (targetElement != null) {
                    styleAction(targetElement);
                }
            });
        };
    }

    static void ApplyStyle(string elementName, System.Action<VisualElement> styleCallback) {
        var element = FindElementByName(elementName);
        if (element != null) {
            styleCallback(element);
        }
    }

    static VisualElement FindElementByName(string name) {
        var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
        foreach (var window in windows) {
            var root = window.rootVisualElement;
            if (root == null) continue;
            
            VisualElement element;
            if ((element = FindByNameRecursive(root, name)) != null) return element;
            if ((element = FindByTooltipRecursive(root, name)) != null) return element;
        }
        return null;
    }

    static VisualElement FindByNameRecursive(VisualElement root, string name) {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        if (root.name == name) return root;

        foreach (var child in root.Children()) {
            var result = FindByNameRecursive(child, name);
            if (result != null) return result;
        }

        return null;
    }

    static VisualElement FindByTooltipRecursive(VisualElement root, string tooltip) {
        if (root == null || string.IsNullOrEmpty(tooltip)) return null;
        if (root.tooltip == tooltip) return root;

        foreach (var child in root.Children()) {
            var result = FindByTooltipRecursive(child, tooltip);
            if (result != null) return result;
        }

        return null;
    }
}