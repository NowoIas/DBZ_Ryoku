using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DBZ.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DBZ.EditorTools
{
    // Inspector des portes : menus déroulants pour targetScene (scènes du build) et linkName (liens des portes
    // de la scène cible qui ramènent ici), et message si la porte n'a pas de porte de retour.
    // Éditeur seulement (dossier Editor) : rien de tout ça n'est dans le jeu.
    [CustomEditor(typeof(Door))]
    public class DoorEditor : Editor
    {
        private const string NoLink = "(aucun)";
        private const string NewLink = "Nouveau…";

        // Une porte trouvée dans une scène : de quoi chercher les liens et les portes de retour
        private struct DoorInfo
        {
            public string Scene;
            public string Name;
            public string TargetScene;
            public string LinkName;
        }

        // Portes lues dans les fichiers des scènes fermées, relues seulement quand le fichier change
        private static readonly Dictionary<string, (DateTime time, List<DoorInfo> doors)> fileCache =
            new Dictionary<string, (DateTime, List<DoorInfo>)>();
        // Identifiants (GUID) du script Door et des prefabs qui le portent, pour les reconnaître dans les fichiers
        private static string doorScriptGuid;
        private static HashSet<string> doorPrefabGuids;

        // Vrai après avoir choisi « Nouveau… » : on tape le nom du lien
        private bool isTypingNewLink;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var door = (Door)target;
            var targetSceneProp = serializedObject.FindProperty("targetScene");
            var linkNameProp = serializedObject.FindProperty("linkName");

            DrawTargetScene(targetSceneProp);

            string here = door.gameObject.scene.name;
            string targetScene = targetSceneProp.stringValue;
            // Portes de la scène cible qui ramènent ici (sauf celle-ci, pour un passage dans la même scène)
            var partners = new List<DoorInfo>();
            if (!string.IsNullOrEmpty(targetScene))
            {
                foreach (var info in DoorsInScene(targetScene, door))
                {
                    if (SameName(info.TargetScene, here))
                        partners.Add(info);
                }
            }

            DrawLinkName(linkNameProp, partners);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("exitDirection"));

            DrawStatus(here, targetScene, linkNameProp.stringValue, partners);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTargetScene(SerializedProperty prop)
        {
            var options = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                options.Add(Path.GetFileNameWithoutExtension(scene.path));

            string current = prop.stringValue;
            int index = options.FindIndex(name => SameName(name, current));
            if (index < 0)
            {
                // Valeur vide ou scène absente du build : on la montre telle quelle en tête de liste
                options.Insert(0, string.IsNullOrEmpty(current) ? "(choisir une scène)" : current + " (absente du build)");
                index = 0;
            }

            int chosen = EditorGUILayout.Popup(new GUIContent("Target Scene", "Scène où mène cette porte (liste des scènes du build)"),
                index, options.ToArray());
            if (chosen != index)
                prop.stringValue = options[chosen];
        }

        private void DrawLinkName(SerializedProperty prop, List<DoorInfo> partners)
        {
            var options = new List<string> { NoLink };
            foreach (var partner in partners)
            {
                if (!string.IsNullOrEmpty(partner.LinkName) && !options.Contains(partner.LinkName))
                    options.Add(partner.LinkName);
            }

            string current = prop.stringValue ?? "";
            // Un lien tapé à la main qui n'existe pas encore de l'autre côté reste dans la liste
            if (current != "" && !options.Contains(current))
                options.Add(current);
            options.Add(NewLink);

            int index = current == "" ? 0 : options.IndexOf(current);
            if (isTypingNewLink)
                index = options.Count - 1;

            var tooltip = "Même nom aux deux bouts d'un passage, quand plusieurs portes relient les deux mêmes scènes. " +
                          "La liste montre les liens des portes de la scène cible qui ramènent ici";
            int chosen = EditorGUILayout.Popup(new GUIContent("Link Name", tooltip), index, options.ToArray());
            if (chosen != index)
            {
                isTypingNewLink = options[chosen] == NewLink;
                if (!isTypingNewLink)
                    prop.stringValue = options[chosen] == NoLink ? "" : options[chosen];
            }

            if (isTypingNewLink)
            {
                // Validé à Entrée ou en quittant le champ
                string typed = EditorGUILayout.DelayedTextField("Nouveau lien", "");
                if (!string.IsNullOrEmpty(typed))
                {
                    prop.stringValue = typed.Trim();
                    isTypingNewLink = false;
                }
            }
        }

        private static void DrawStatus(string here, string targetScene, string linkName, List<DoorInfo> partners)
        {
            if (string.IsNullOrEmpty(targetScene))
            {
                EditorGUILayout.HelpBox("Choisir la scène où mène cette porte.", MessageType.Warning);
                return;
            }

            var matches = partners.FindAll(p => (p.LinkName ?? "") == (linkName ?? ""));
            string link = string.IsNullOrEmpty(linkName) ? "" : $" avec le lien « {linkName} »";
            if (matches.Count == 0)
                EditorGUILayout.HelpBox($"Aucune porte de {targetScene} ne ramène ici{link} : l'autre bout reste à poser " +
                                        $"(une porte de {targetScene} avec Target Scene = {here}).", MessageType.Warning);
            else if (matches.Count == 1)
                EditorGUILayout.HelpBox($"Relié à « {matches[0].Name} » dans {targetScene}.", MessageType.Info);
            else
                EditorGUILayout.HelpBox($"{matches.Count} portes de {targetScene} ramènent ici{link} : seule la première servira. " +
                                        "Leur donner des liens différents.", MessageType.Warning);
        }

        // Portes d'une scène : lues sur les objets si elle est ouverte (modifications non sauvegardées comprises),
        // sinon dans son fichier
        private static List<DoorInfo> DoorsInScene(string sceneName, Door except)
        {
            var result = new List<DoorInfo>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !SameName(scene.name, sceneName))
                    continue;

                foreach (var door in FindObjectsByType<Door>(FindObjectsInactive.Include))
                {
                    if (door != except && door.gameObject.scene == scene)
                        result.Add(new DoorInfo { Scene = scene.name, Name = door.name, TargetScene = door.TargetScene, LinkName = door.LinkName });
                }
                return result;
            }

            foreach (var buildScene in EditorBuildSettings.scenes)
            {
                if (SameName(Path.GetFileNameWithoutExtension(buildScene.path), sceneName))
                    return DoorsInFile(buildScene.path);
            }
            return result;
        }

        // Lecture du fichier de scène (texte YAML), sans l'ouvrir dans l'éditeur
        private static List<DoorInfo> DoorsInFile(string path)
        {
            if (!File.Exists(path))
                return new List<DoorInfo>();

            var time = File.GetLastWriteTimeUtc(path);
            if (fileCache.TryGetValue(path, out var cached) && cached.time == time)
                return cached.doors;

            FindDoorGuids();
            var doors = new List<DoorInfo>();
            string sceneName = Path.GetFileNameWithoutExtension(path);
            foreach (string block in File.ReadAllText(path).Split(new[] { "\n--- " }, StringSplitOptions.None))
            {
                // Porte posée depuis le prefab : ses valeurs sont des modifications du prefab
                var source = Regex.Match(block, @"m_SourcePrefab: \{fileID: \d+, guid: (\w+)");
                if (block.Contains("PrefabInstance:") && source.Success && doorPrefabGuids.Contains(source.Groups[1].Value))
                {
                    doors.Add(new DoorInfo
                    {
                        Scene = sceneName,
                        Name = Modification(block, "m_Name") ?? "Door",
                        TargetScene = Modification(block, "targetScene") ?? "",
                        LinkName = Modification(block, "linkName") ?? "",
                    });
                }
                // Composant Door ajouté à la main sur un objet
                else if (block.Contains("MonoBehaviour:") && block.Contains("guid: " + doorScriptGuid))
                {
                    doors.Add(new DoorInfo
                    {
                        Scene = sceneName,
                        Name = "Door",
                        TargetScene = Field(block, "targetScene") ?? "",
                        LinkName = Field(block, "linkName") ?? "",
                    });
                }
            }

            fileCache[path] = (time, doors);
            return doors;
        }

        private static string Modification(string block, string propertyPath)
        {
            var match = Regex.Match(block, @"propertyPath: " + propertyPath + @"\s*\n\s*value: ?(.*)");
            return match.Success ? Unquote(match.Groups[1].Value) : null;
        }

        private static string Field(string block, string field)
        {
            var match = Regex.Match(block, @"\n\s*" + field + @": ?(.*)");
            return match.Success ? Unquote(match.Groups[1].Value) : null;
        }

        private static string Unquote(string value)
        {
            value = value.Trim();
            if (value.Length >= 2 && (value[0] == '\'' || value[0] == '"') && value[value.Length - 1] == value[0])
                value = value.Substring(1, value.Length - 2);
            return value;
        }

        private static void FindDoorGuids()
        {
            if (doorPrefabGuids != null)
                return;

            doorPrefabGuids = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.GetComponent<Door>() != null)
                    doorPrefabGuids.Add(guid);
            }

            foreach (string guid in AssetDatabase.FindAssets("Door t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == typeof(Door))
                    doorScriptGuid = guid;
            }
        }

        private static bool SameName(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
