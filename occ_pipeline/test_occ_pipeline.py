"""
Test complet du pipeline OCC :
1. Vérifie que le serveur Mistral 7B tourne sur localhost:5050
2. Simule une conversation avec évolution émotionnelle
3. Vérifie que le preprompt change selon les émotions détectées

Lance d'abord : python occ_emotion_server.py --model mistral-occ-lora
Puis          : python test_occ_pipeline.py
"""

import requests

OCC_SERVER = "http://localhost:5050"

BASE_PREPROMPT = "Tu t'appelles Mike et tu es un guide pour les nouveaux étudiants de CentraleSupelec. Tu fais des réponses courtes."

# Dictionnaire de valence (même que ComputationalModelOCC.cs)
VALENCE = {
    "JOY": 1.0, "DISTRESS": -1.0, "HOPE": 0.6, "FEAR": -0.6,
    "SATISFACTION": 0.8, "DISAPPOINTMENT": -0.8, "RELIEF": 0.7,
    "FEARS_CONFIRMED": -0.9, "HAPPY_FOR": 0.5, "PITY": -0.4,
    "RESENTMENT": -0.5, "GLOATING": 0.3, "PRIDE": 0.7, "SHAME": -0.8,
    "ADMIRATION": 0.6, "REPROACH": -0.5, "LOVE": 0.9, "HATE": -0.9,
    "GRATITUDE": 0.8, "ANGER": -1.0, "GRATIFICATION": 0.9, "REMORSE": -0.9,
}

PERSONALITY_HINTS = {
    "JOY": "joyful, enthusiastic, and warm",
    "GRATIFICATION": "joyful, enthusiastic, and warm",
    "DISTRESS": "distressed, tense, and apprehensive",
    "FEARS_CONFIRMED": "distressed, tense, and apprehensive",
    "ANGER": "irritated, firm, and direct",
    "FEAR": "worried, hesitant, and cautious",
    "DISAPPOINTMENT": "worried, hesitant, and cautious",
    "HOPE": "optimistic, calm, and confident",
    "SATISFACTION": "optimistic, calm, and confident",
    "ADMIRATION": "impressed, grateful, and friendly",
    "GRATITUDE": "impressed, grateful, and friendly",
    "PRIDE": "proud, self-assured, and positive",
    "SHAME": "remorseful, humble, and apologetic",
    "REMORSE": "remorseful, humble, and apologetic",
    "LOVE": "affectionate, caring, and warm",
    "HATE": "cold, critical, and distant",
    "REPROACH": "cold, critical, and distant",
    "RESENTMENT": "cold, critical, and distant",
    "PITY": "empathetic and attentive",
    "HAPPY_FOR": "empathetic and attentive",
}

def get_personality_hint(emotion):
    return PERSONALITY_HINTS.get(emotion, "neutral and balanced")

def classify_emotion(text):
    """Envoie un texte au serveur OCC et retourne l'émotion."""
    try:
        resp = requests.post(
            f"{OCC_SERVER}/classify",
            json={"text": text},
            timeout=30
        )
        resp.raise_for_status()
        data = resp.json()
        return data["emotion"], data["confidence"]
    except Exception as e:
        return None, 0.0

def compute_dominant(history, decay=0.7):
    """Calcule l'émotion dominante depuis l'historique (comme ComputeDominant() en C#)."""
    scores = {}
    for i, (emotion, confidence) in enumerate(history):
        w = confidence * (decay ** i)
        scores[emotion] = scores.get(emotion, 0) + w

    if not scores:
        return "JOY", 0.0

    best = max(scores, key=scores.get)
    total = sum(scores.values())
    intensity = scores[best] / total if total > 0 else 0.0
    return best, intensity

def build_preprompt(history):
    """Simule ce que fait SendToChat() avec GetPersonalityHint()."""
    if not history:
        return BASE_PREPROMPT + " You are currently feeling : neutral and balanced."
    
    dominant, intensity = compute_dominant(history)
    hint = get_personality_hint(dominant)
    return BASE_PREPROMPT + f" You are currently feeling : {hint}."

# ── Simulation de conversation ───────────────────────────────────────────────

phrases_test = [
    "Je suis tellement heureux d'être admis à CentraleSupelec !",
    "C'est une école incroyable, je suis admiratif de tout ce qu'on y apprend.",
    "J'ai raté mon premier examen, je suis vraiment déçu de moi.",
    "Je ne sais pas si je vais réussir, j'ai peur de l'avenir.",
    "Mon professeur m'a félicité aujourd'hui, je suis vraiment fier !",
    "Je déteste ce projet de groupe, personne ne travaille sérieusement.",
    "Finalement on a eu une bonne note, je suis soulagé !",
]

print("=" * 60)
print("TEST DU PIPELINE OCC")
print("=" * 60)

# Test 1 : serveur accessible
print("\n1. Vérification du serveur OCC...")
try:
    resp = requests.get(f"{OCC_SERVER}/health", timeout=5)
    data = resp.json()
    print(f"   ✓ Serveur OK — mode : {data['mode']}")
except Exception as e:
    print(f"   ✗ Serveur inaccessible : {e}")
    print("   Lance d'abord : python occ_emotion_server.py --model mistral-occ-lora")
    exit(1)

# Test 2 : classification + évolution du preprompt
print("\n2. Simulation de conversation avec évolution émotionnelle...")
print("-" * 60)

history = []  # historique des (emotion, confidence)
prev_hint = None

for i, phrase in enumerate(phrases_test):
    print(f"\n[Tour {i+1}] Phrase : \"{phrase}\"")
    
    # Classification OCC
    emotion, confidence = classify_emotion(phrase)
    if emotion is None:
        print("   ✗ Erreur de classification")
        continue
    
    print(f"   → Émotion détectée : {emotion} (confiance: {confidence:.2f})")
    
    # Mise à jour historique
    history.insert(0, (emotion, confidence))
    if len(history) > 10:
        history.pop()
    
    # Calcul dominant
    dominant, intensity = compute_dominant(history)
    hint = get_personality_hint(dominant)
    
    print(f"   → Émotion dominante : {dominant} (intensité: {intensity:.2f})")
    print(f"   → Personality hint  : \"{hint}\"")
    
    # Preprompt résultant
    preprompt = build_preprompt(history)
    
    if hint != prev_hint:
        print(f"   ★ PREPROMPT CHANGÉ !")
        print(f"     \"{preprompt}\"")
        prev_hint = hint
    else:
        print(f"   = Preprompt inchangé ({hint})")

print("\n" + "=" * 60)
print("RÉSUMÉ")
print("=" * 60)
dominant, intensity = compute_dominant(history)
valence = sum(VALENCE.get(e, 0) * c for e, c in history) / len(history) if history else 0
print(f"Émotion finale dominante : {dominant}")
print(f"Intensité : {intensity:.2f}")
print(f"Valence globale : {valence:+.2f} ({'positif' if valence > 0 else 'négatif'})")
print(f"Preprompt final :")
print(f"  \"{build_preprompt(history)}\"")
print("=" * 60)