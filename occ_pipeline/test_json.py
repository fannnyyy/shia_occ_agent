"""
Script de debug : affiche la réponse brute de l'API pour JOY.
Usage : python debug_api.py
"""
import requests
import json

URL   = "https://beautiful-blackwell.mydocker-run-vd.centralesupelec.fr/api/chat/completions"
MODEL = "gpt-oss-120b"
KEY   = "sk-dcf53709e00447ab8a27224d20031278"

payload = {
    "model": MODEL,
    "messages": [
        {"role": "system", "content": "Tu réponds UNIQUEMENT en JSONL, un objet JSON par ligne, sans texte supplémentaire."},
        {"role": "user",   "content": 'Génère 3 exemples pour l\'émotion JOY. Format exact par ligne : {"texte": "...", "emotion_occ": "JOY", "intensite": 4}'},
    ],
    "temperature": 0.5,
    "max_tokens": 500,
}

headers = {
    "Content-Type": "application/json",
    "Authorization": f"Bearer {KEY}",
}

print(f"POST → {URL}\n")
try:
    r = requests.post(URL, headers=headers, json=payload, timeout=60)
    print(f"Status : {r.status_code}")
    print(f"Headers : {dict(r.headers)}\n")
    
    # Réponse brute texte
    print("=== RAW TEXT ===")
    print(repr(r.text[:2000]))
    print()
    
    # Tentative de parse JSON
    print("=== JSON PARSED ===")
    data = r.json()
    print(json.dumps(data, indent=2, ensure_ascii=False)[:2000])

    # Contenu du message
    print("\n=== CONTENT DU MESSAGE ===")
    content = data["choices"][0]["message"].get("content")
    print(repr(content))

except Exception as e:
    print(f"ERREUR : {e}")
    print(f"Réponse brute : {r.text[:500] if 'r' in dir() else 'N/A'}")