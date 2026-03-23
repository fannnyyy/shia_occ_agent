using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class GenerateConversationJSON : MonoBehaviour
{
    private List<Dictionary<string, string>> _log = new List<Dictionary<string, string>>();
    private int _tour = 0;
    private string _filePath;

    void Awake()
    {
        _filePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop) +  "/conversation_baseline.json";
        Debug.Log("[LOG] Fichier : " + _filePath);
    }

    public void LogTurn(
        string hintInjecte,
        string prepromptComplet,
        string replicParticipant,
        string replicAgent,
        string emotionDetectee,
        string emotionDominante)
    {
        _tour++;
        var entry = new Dictionary<string, string>
        {
            { "tour", _tour.ToString() },
            { "descripteur_emotionnel_injecte", hintInjecte },
            { "preprompt_complet", prepromptComplet },
            { "replique_participant", replicParticipant },
            { "replique_agent", replicAgent },
            { "emotion_detectee_participant", emotionDetectee },
            { "emotion_dominante_global", emotionDominante }
        };
        _log.Add(entry);
        SaveToFile();
    }

    private void SaveToFile()
    {
        var jsonArray = new System.Text.StringBuilder();
        jsonArray.Append("[\n");
        for (int i = 0; i < _log.Count; i++)
        {
            jsonArray.Append("  {\n");
            int j = 0;
            foreach (var kvp in _log[i])
            {
                string comma = (j < _log[i].Count - 1) ? "," : "";
                jsonArray.Append($"    \"{kvp.Key}\": \"{kvp.Value.Replace("\"", "\\\"").Replace("\n", " ")}\"{comma}\n");
                j++;
            }
            jsonArray.Append(i < _log.Count - 1 ? "  },\n" : "  }\n");
        }
        jsonArray.Append("]");
        File.WriteAllText(_filePath, jsonArray.ToString());
    }
}