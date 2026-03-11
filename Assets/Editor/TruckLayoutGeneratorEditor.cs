using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TruckLayoutGenerator))]
public class TruckLayoutGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TruckLayoutGenerator generator = (TruckLayoutGenerator)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Generation Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Generate Filled Layout"))
        {
            Undo.RecordObject(generator, "Generate Filled Layout");
            bool success = generator.GenerateFilledLayout();

            if (success)
            {
                EditorUtility.SetDirty(generator);
                generator.LogSolution();
            }
        }

        if (GUILayout.Button("Generate And Save Manifest Asset"))
        {
            bool success = generator.GenerateFilledLayout();
            if (!success)
            {
                Debug.LogWarning("Could not generate a solvable full layout, so no manifest was saved.");
                return;
            }

            PackManifest manifest = generator.BuildManifestFromSolution();
            if (!manifest)
            {
                Debug.LogError("Failed to build manifest from solution.");
                return;
            }

            string folder = generator.ManifestSaveFolder;
            string assetName = generator.ManifestAssetName;

            EnsureFolderExists(folder);

            string cleanName = string.IsNullOrWhiteSpace(assetName) ? "GeneratedPackManifest" : assetName;
            string path = Path.Combine(folder, cleanName + ".asset").Replace("\\", "/");
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            AssetDatabase.CreateAsset(manifest, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = manifest;

            Debug.Log($"Saved generated manifest to: {path}");
        }

        if (GUILayout.Button("Log Current Solution"))
        {
            generator.LogSolution();
        }
    }

    static void EnsureFolderExists(string fullFolderPath)
    {
        if (string.IsNullOrWhiteSpace(fullFolderPath))
            return;

        string normalized = fullFolderPath.Replace("\\", "/");

        if (AssetDatabase.IsValidFolder(normalized))
            return;

        string[] parts = normalized.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
        {
            Debug.LogError("Manifest save folder must start with 'Assets'.");
            return;
        }

        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}