using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts
{
    /*
     * ComputationalModelOCC — modèle émotionnel basé sur OCC (Ortony, Clore & Collins).
     *
     * Maintient un état émotionnel courant et un historique pondéré dans le temps
     * (les émotions récentes ont plus d'influence que les anciennes).
     *
     * Utilisé par :
     *   - OccEmotionClient.cs  : reçoit les nouvelles émotions classifiées
     *   - LLMDialogManager.cs  : consulte l'émotion pour adapter le preprompt
     */
    public class ComputationalModelOCC : MonoBehaviour
    {
        // ── Paramètres exposés dans l'Inspector ──────────────────────────────
        [Header("Paramètres du modèle")]
        [Tooltip("Nombre d'émotions gardées en mémoire")]
        public int historySize = 10;

        [Tooltip("Facteur de décroissance : les émotions anciennes pèsent moins (0-1)")]
        [Range(0f, 1f)]
        public float decayFactor = 0.7f;

        [Tooltip("Intensité minimale pour déclencher une expression faciale")]
        [Range(0f, 1f)]
        public float expressionThreshold = 0.4f;

        // ── État interne ──────────────────────────────────────────────────────
        private OccEmotion _currentEmotion = OccEmotion.JOY;
        private float _currentIntensity = 0f;
        private readonly Queue<(OccEmotion emotion, float confidence, float timestamp)> _history
            = new Queue<(OccEmotion, float, float)>();

        // Dictionnaire de valence : positif (+) / négatif (-)
        private static readonly Dictionary<OccEmotion, float> Valence = new()
        {
            { OccEmotion.JOY,              +1.0f },
            { OccEmotion.DISTRESS,         -1.0f },
            { OccEmotion.HOPE,             +0.6f },
            { OccEmotion.FEAR,             -0.6f },
            { OccEmotion.SATISFACTION,     +0.8f },
            { OccEmotion.DISAPPOINTMENT,   -0.8f },
            { OccEmotion.RELIEF,           +0.7f },
            { OccEmotion.FEARS_CONFIRMED,  -0.9f },
            { OccEmotion.HAPPY_FOR,        +0.5f },
            { OccEmotion.PITY,             -0.4f },
            { OccEmotion.RESENTMENT,       -0.5f },
            { OccEmotion.GLOATING,         +0.3f },
            { OccEmotion.PRIDE,            +0.7f },
            { OccEmotion.SHAME,            -0.8f },
            { OccEmotion.ADMIRATION,       +0.6f },
            { OccEmotion.REPROACH,         -0.5f },
            { OccEmotion.LOVE,             +0.9f },
            { OccEmotion.HATE,             -0.9f },
            { OccEmotion.GRATITUDE,        +0.8f },
            { OccEmotion.ANGER,            -1.0f },
            { OccEmotion.GRATIFICATION,    +0.9f },
            { OccEmotion.REMORSE,          -0.9f },
        };

        // ── Événements ────────────────────────────────────────────────────────
        /// Déclenché quand l'émotion dominante change
        public event Action<OccEmotion, float> OnDominantEmotionChanged;

        // ── API publique ──────────────────────────────────────────────────────

        /// <summary>Reçoit une nouvelle émotion classifiée et met à jour l'état.</summary>
        public void UpdateEmotion(OccEmotion emotion, float confidence)
        {
            _history.Enqueue((emotion, confidence, Time.time));
            if (_history.Count > historySize)
                _history.Dequeue();

            var prev = _currentEmotion;
            (_currentEmotion, _currentIntensity) = ComputeDominant();

            if (prev != _currentEmotion)
                OnDominantEmotionChanged?.Invoke(_currentEmotion, _currentIntensity);
        }

        /// <summary>Émotion OCC courante (la plus présente dans l'historique pondéré).</summary>
        public OccEmotion GetCurrentEmotion() => _currentEmotion;

        /// <summary>Intensité de l'émotion courante [0, 1].</summary>
        public float GetCurrentIntensity() => _currentIntensity;

        /// <summary>Valence affective globale en [-1, +1]. Positif = bien-être.</summary>
        public float GetValence()
        {
            float total = 0f, weight = 0f, t = 0f;
            foreach (var (emotion, confidence, timestamp) in _history)
            {
                float w = confidence * MathF.Pow(decayFactor, t);
                total += Valence[emotion] * w;
                weight += w;
                t++;
            }
            return weight > 0 ? Mathf.Clamp(total / weight, -1f, 1f) : 0f;
        }

        /// <summary>
        /// Retourne un mot-clé de personnalité à injecter dans le preprompt du LLM.
        /// Ex : "joyful and enthusiastic" ou "distressed and tense"
        /// </summary>
        public string GetPersonalityHint()
        {
            return _currentEmotion switch
            {
                OccEmotion.JOY or OccEmotion.GRATIFICATION =>
                    "joyful, enthusiastic, and warm",
                OccEmotion.DISTRESS or OccEmotion.FEARS_CONFIRMED =>
                    "distressed, tense, and apprehensive",
                OccEmotion.ANGER =>
                    "irritated, firm, and direct",
                OccEmotion.FEAR or OccEmotion.DISAPPOINTMENT =>
                    "worried, hesitant, and cautious",
                OccEmotion.HOPE or OccEmotion.SATISFACTION =>
                    "optimistic, calm, and confident",
                OccEmotion.ADMIRATION or OccEmotion.GRATITUDE =>
                    "impressed, grateful, and friendly",
                OccEmotion.PRIDE =>
                    "proud, self-assured, and positive",
                OccEmotion.SHAME or OccEmotion.REMORSE =>
                    "remorseful, humble, and apologetic",
                OccEmotion.LOVE =>
                    "affectionate, caring, and warm",
                OccEmotion.HATE or OccEmotion.REPROACH or OccEmotion.RESENTMENT =>
                    "cold, critical, and distant",
                OccEmotion.PITY or OccEmotion.HAPPY_FOR =>
                    "empathetic and attentive",
                _ => "neutral and balanced",
            };
        }

        /// <summary>Indique si l'intensité est suffisante pour afficher une expression.</summary>
        public bool ShouldExpressEmotion() => _currentIntensity >= expressionThreshold;

        /// <summary>
        /// Réinitialise complètement l'état émotionnel (historique, émotion courante, intensité).
        /// Appelé lors de la transition entre Conversation A et B pour repartir de zéro.
        /// </summary>
        public void ResetEmotions()
        {
            _history.Clear();
            _currentEmotion = OccEmotion.JOY;
            _currentIntensity = 0f;
            Debug.Log("[OCC] Émotions réinitialisées.");
        }

        // ── Compatibilité avec l'ancien ComputationalModel ────────────────────
        // (Pour ne pas casser le code existant qui appelait getEmotion())

        [Obsolete("Utilise GetCurrentEmotion() à la place")]
        public int getEmotion() => (int)_currentEmotion;

        // ── Méthodes héritées (données utilisateur / LLM) ─────────────────────

        public void UserValues(string values)
        {
            // Tu peux ici parser des signaux utilisateur (ex: tonalité audio, geste...)
            Debug.Log($"[OCC] UserValues reçu : {values}");
        }

        public void LLMValues(string values)
        {
            // Tu peux ici forcer une émotion depuis le texte LLM brut si besoin
            Debug.Log($"[OCC] LLMValues reçu : {values}");
        }

        // ── Calcul interne ────────────────────────────────────────────────────

        private (OccEmotion dominant, float intensity) ComputeDominant()
        {
            var scores = new Dictionary<OccEmotion, float>();
            float t = 0f;

            foreach (var (emotion, confidence, _) in _history)
            {
                float w = confidence * MathF.Pow(decayFactor, t);
                scores.TryGetValue(emotion, out float current);
                scores[emotion] = current + w;
                t++;
            }

            OccEmotion best = OccEmotion.JOY;
            float bestScore = 0f, totalScore = 0f;

            foreach (var (emotion, score) in scores)
            {
                totalScore += score;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = emotion;
                }
            }

            float intensity = totalScore > 0 ? Mathf.Clamp01(bestScore / totalScore) : 0f;
            return (best, intensity);
        }

        public void Reset()
        {
            _history.Clear();
            _currentEmotion = OccEmotion.JOY;
            _currentIntensity = 0f;
        }
    }
}
