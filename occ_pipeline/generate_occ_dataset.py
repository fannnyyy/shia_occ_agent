"""
Génération du dataset OCC via un LLM teacher (API OpenAI-compatible).

Usage :
    python generate_occ_dataset.py \
        --teacher-url https://beautiful-blackwell.mydocker-run-vd.centralesupelec.fr/api \
        --teacher-model gpt-oss-120b \
        --teacher-key sk-xxxx \
        --examples-per-emotion 30

Résultat : occ_dataset.json
"""

import argparse
import json
import time
import random
import requests
from collections import Counter

OCC_EMOTIONS = {
    "JOY":             "Événement positif dont les conséquences sont désirables pour soi",
    "DISTRESS":        "Événement négatif dont les conséquences sont indésirables pour soi",
    "HOPE":            "Anticipation d'un événement positif futur incertain",
    "FEAR":            "Anticipation d'un événement négatif futur incertain",
    "SATISFACTION":    "Événement positif espéré qui s'est finalement réalisé",
    "DISAPPOINTMENT":  "Événement positif espéré qui ne s'est pas réalisé",
    "RELIEF":          "Événement négatif redouté qui ne s'est finalement pas réalisé",
    "FEARS_CONFIRMED": "Événement négatif redouté qui s'est réalisé",
    "HAPPY_FOR":       "Événement positif pour quelqu'un que l'on apprécie",
    "PITY":            "Événement négatif pour quelqu'un que l'on apprécie",
    "RESENTMENT":      "Événement positif pour quelqu'un que l'on n'apprécie pas",
    "GLOATING":        "Événement négatif pour quelqu'un que l'on n'apprécie pas",
    "PRIDE":           "Acte positif, vertueux accompli par soi-même",
    "SHAME":           "Acte négatif, honteux accompli par soi-même",
    "ADMIRATION":      "Acte positif, louable accompli par quelqu'un d'autre",
    "REPROACH":        "Acte blâmable, négatif accompli par quelqu'un d'autre",
    "LOVE":            "Appréciation, affection forte pour un objet ou une personne",
    "HATE":            "Aversion, répulsion forte pour un objet ou une personne",
    "GRATITUDE":       "Acte positif d'autrui qui nous bénéficie (admiration + joie)",
    "ANGER":           "Acte négatif d'autrui qui nous nuit (reproche + détresse)",
    "GRATIFICATION":   "On accomplit un acte vertueux et cela nous bénéficie (fierté + joie)",
    "REMORSE":         "On accomplit un acte honteux et on en subit les conséquences (honte + détresse)",
}

SYSTEM_PROMPT = (
    "Tu es un expert en psychologie cognitive et en modèles émotionnels OCC "
    "(Ortony, Clore & Collins). Tu génères des exemples en français ou anglais. "
    "Tu réponds UNIQUEMENT en JSONL : un objet JSON par ligne, sans backticks, sans numérotation."
)

USER_TEMPLATE = (
    'Génère {n} énoncés pour l\'émotion OCC "{emotion}".\n'
    'Définition : {definition}\n\n'
    'Format STRICT, une ligne par exemple :\n'
    '{{"texte": "...", "emotion_occ": "{emotion}", "intensite": <1-5>}}\n\n'
    'Contraintes : 8-50 mots, ne mentionne pas le nom de l\'émotion, alterne FR/EN.'
)


def call_api(url, model, key, system, user):
    endpoint = url.rstrip("/") + "/chat/completions"
    headers  = {"Content-Type": "application/json", "Authorization": f"Bearer {key}"}
    payload  = {
        "model": model,
        "messages": [
            {"role": "system", "content": system},
            {"role": "user",   "content": user},
        ],
        "temperature": 0.85,
        "max_tokens": 3000,
    }
    r = requests.post(endpoint, headers=headers, json=payload, timeout=180)
    r.raise_for_status()
    return r.json()["choices"][0]["message"].get("content") or ""


def parse_jsonl(raw, emotion):
    raw = raw.replace("\u202f", " ").replace("\u00a0", " ")
    raw = raw.replace("```json", "").replace("```", "")

    examples = []
    for line in raw.splitlines():
        line = line.strip().rstrip(",")
        if not line or "{" not in line:
            continue

        start = line.find("{")
        end   = line.rfind("}")
        if end <= start:
            continue
        candidate = line[start : end + 1]

        obj = None
        for s in (candidate, candidate.replace("\\'", "'")):
            try:
                obj = json.loads(s)
                break
            except json.JSONDecodeError:
                continue
        if obj is None:
            continue

        texte = str(obj.get("texte", "")).strip()
        if len(texte.split()) < 4:
            continue

        try:
            intensite = max(1, min(5, int(obj.get("intensite", 3))))
        except (ValueError, TypeError):
            intensite = 3

        examples.append({"texte": texte, "emotion_occ": emotion, "intensite": intensite})

    return examples


def generate(args, emotion, definition, n):
    user_msg = USER_TEMPLATE.format(n=n, emotion=emotion, definition=definition)

    for attempt in range(4):
        try:
            raw     = call_api(args.teacher_url, args.teacher_model,
                               args.teacher_key, SYSTEM_PROMPT, user_msg)
            if args.debug:
                print(f"\n[DEBUG]\n{repr(raw[:500])}\n")
            results = parse_jsonl(raw, emotion)
            if results:
                return results
            print(f"(tentative {attempt+1} vide)", end=" ", flush=True)
        except requests.exceptions.Timeout:
            print(f"(timeout {attempt+1})", end=" ", flush=True)
        except requests.exceptions.ConnectionError as e:
            print(f"(connexion perdue {attempt+1})", end=" ", flush=True)
        except requests.exceptions.HTTPError as e:
            code = e.response.status_code
            print(f"(HTTP {code})", end=" ", flush=True)
            if code < 500:
                break
        except Exception as e:
            print(f"(erreur: {e})", end=" ", flush=True)
        time.sleep(2)

    return []


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--teacher-url",           required=True)
    p.add_argument("--teacher-model",         required=True)
    p.add_argument("--teacher-key",           default="")
    p.add_argument("--examples-per-emotion",  type=int, default=25)
    p.add_argument("--output",                default="occ_dataset.json")
    p.add_argument("--debug",                 action="store_true")
    args = p.parse_args()

    print(f"Modèle  : {args.teacher_model}")
    print(f"URL     : {args.teacher_url}")
    print(f"Émotions: {len(OCC_EMOTIONS)} x {args.examples_per_emotion} exemples\n")

    dataset, failed = [], []

    for i, (emotion, definition) in enumerate(OCC_EMOTIONS.items()):
        print(f"[{i+1:02}/{len(OCC_EMOTIONS)}] {emotion:<20} ...", end=" ", flush=True)
        examples = generate(args, emotion, definition, args.examples_per_emotion)
        if examples:
            dataset.extend(examples)
            print(f"{len(examples)} exemples OK")
        else:
            print("ECHEC")
            failed.append(emotion)
        time.sleep(0.3)

    random.shuffle(dataset)
    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(dataset, f, ensure_ascii=False, indent=2)

    print(f"\nSauvegarde : {args.output}  ({len(dataset)} exemples)")

    counts = Counter(ex["emotion_occ"] for ex in dataset)
    print("\nDistribution :")
    for emotion in OCC_EMOTIONS:
        n   = counts.get(emotion, 0)
        bar = "x" * (n // 2)
        print(f"  {emotion:<20} {n:>3}  {bar}{'  VIDE' if n == 0 else ''}")

    if failed:
        print(f"\nEchecs : {', '.join(failed)}")


if __name__ == "__main__":
    main()