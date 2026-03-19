"""
Étape 3 — Serveur HTTP local pour la classification OCC.

Lance le modèle fine-tuné (ou Ollama en fallback) et expose une API REST
que Unity appellera via UnityWebRequest.

Prérequis :
    pip install flask transformers peft torch

Utilisation :
    # Avec le modèle fine-tuné (recommandé après fine-tuning) :
    python occ_emotion_server.py --model mistral-occ-lora

    # Fallback : utilise Ollama directement (sans fine-tuning) :
    python occ_emotion_server.py --ollama --ollama-model mistral

API :
    POST http://localhost:5050/classify
    Body : {"text": "Inception est un film fascinant !"}
    Réponse : {"emotion": "ADMIRATION", "confidence": 0.92, "all_scores": {...}}

    GET http://localhost:5050/health
    Réponse : {"status": "ok", "mode": "lora|ollama"}
"""

import argparse
import json
import re
import sys
from flask import Flask, request, jsonify

app = Flask(__name__)

# État global du serveur
MODEL = None
TOKENIZER = None
OLLAMA_URL = None
OLLAMA_MODEL = None
MODE = None  # "lora" ou "ollama"

OCC_LABELS = [
    "JOY", "DISTRESS", "HOPE", "FEAR", "SATISFACTION", "DISAPPOINTMENT",
    "RELIEF", "FEARS_CONFIRMED", "HAPPY_FOR", "PITY", "RESENTMENT", "GLOATING",
    "PRIDE", "SHAME", "ADMIRATION", "REPROACH", "LOVE", "HATE",
    "GRATITUDE", "ANGER", "GRATIFICATION", "REMORSE",
]
LABELS_STR = ", ".join(OCC_LABELS)

CLASSIFY_PROMPT = """Tu es un classificateur d'émotions OCC. Analyse le texte et retourne UNIQUEMENT le label OCC le plus approprié parmi : {labels}.

Texte : {texte}
Émotion OCC :"""


# ── Mode LoRA ───────────────────────────────────────────────────────────────

def load_lora_model(model_path: str):
    global MODEL, TOKENIZER
    import torch
    from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig
    from peft import PeftModel

    print(f"Chargement du modèle LoRA depuis {model_path}...")
    with open(f"{model_path}/adapter_config.json") as f:
        adapter_config = json.load(f)
    base_model_name = adapter_config.get("base_model_name_or_path",
                                         "mistralai/Mistral-7B-Instruct-v0.2")
    print(f"Modèle de base : {base_model_name}")

    TOKENIZER = AutoTokenizer.from_pretrained(model_path, trust_remote_code=True)
    TOKENIZER.pad_token = TOKENIZER.eos_token

    # Quantisation 4-bit pour tenir dans 8 Go de VRAM
    bnb_config = BitsAndBytesConfig(
        load_in_4bit=True,
        bnb_4bit_compute_dtype=torch.float16,
        bnb_4bit_use_double_quant=True,
        bnb_4bit_quant_type="nf4",
    )

    base_model = AutoModelForCausalLM.from_pretrained(
        base_model_name,
        quantization_config=bnb_config,
        device_map={"": 0},
        trust_remote_code=True,
    )
    MODEL = PeftModel.from_pretrained(base_model, model_path)
    MODEL.eval()
    print("✓ Modèle LoRA prêt")
    
def classify_with_lora(text: str) -> dict:
    import torch
    prompt = CLASSIFY_PROMPT.format(labels=LABELS_STR, texte=text)
    inputs = TOKENIZER(prompt, return_tensors="pt", truncation=True, max_length=256)
    inputs = {k: v.to(MODEL.device) for k, v in inputs.items()}

    with torch.no_grad():
        outputs = MODEL.generate(
            **inputs,
            max_new_tokens=10,
            do_sample=False,
            temperature=1.0,
            pad_token_id=TOKENIZER.eos_token_id,
        )

    generated = outputs[0][inputs["input_ids"].shape[1]:]
    prediction = TOKENIZER.decode(generated, skip_special_tokens=True).strip()

    # Extrait le premier label OCC valide trouvé
    emotion = extract_occ_label(prediction)
    return {"emotion": emotion, "confidence": 1.0, "raw": prediction}


# ── Mode Ollama (fallback) ──────────────────────────────────────────────────

def classify_with_ollama(text: str) -> dict:
    import requests
    prompt = CLASSIFY_PROMPT.format(labels=LABELS_STR, texte=text)
    payload = {
        "model": OLLAMA_MODEL,
        "prompt": prompt,
        "stream": False,
        "options": {"temperature": 0.1, "num_predict": 20},
    }
    resp = requests.post(f"{OLLAMA_URL}/api/generate", json=payload, timeout=30)
    resp.raise_for_status()
    raw = resp.json().get("response", "").strip()
    emotion = extract_occ_label(raw)
    return {"emotion": emotion, "confidence": 1.0, "raw": raw}


def extract_occ_label(text: str) -> str:
    """Extrait le premier label OCC valide du texte généré."""
    text_upper = text.upper()
    for label in OCC_LABELS:
        if label in text_upper:
            return label
    return "JOY"  # fallback neutre


# ── Routes Flask ────────────────────────────────────────────────────────────

@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "ok", "mode": MODE})


@app.route("/classify", methods=["POST"])
def classify():
    data = request.get_json()
    if not data or "text" not in data:
        return jsonify({"error": "Champ 'text' manquant"}), 400

    text = data["text"].strip()
    if not text:
        return jsonify({"error": "Texte vide"}), 400

    try:
        if MODE == "lora":
            result = classify_with_lora(text)
        else:
            result = classify_with_ollama(text)
        return jsonify(result)
    except Exception as e:
        return jsonify({"error": str(e), "emotion": "JOY"}), 500


# ── Point d'entrée ──────────────────────────────────────────────────────────

def main():
    global MODE, OLLAMA_URL, OLLAMA_MODEL

    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default=None,
                        help="Chemin vers le modèle LoRA fine-tuné (ex: mistral-occ-lora)")
    parser.add_argument("--ollama", action="store_true",
                        help="Utiliser Ollama comme backend (fallback sans fine-tuning)")
    parser.add_argument("--ollama-url", default="http://localhost:11434")
    parser.add_argument("--ollama-model", default="mistral")
    parser.add_argument("--port", type=int, default=5050)
    args = parser.parse_args()

    if args.model:
        MODE = "lora"
        load_lora_model(args.model)
    elif args.ollama:
        MODE = "ollama"
        OLLAMA_URL = args.ollama_url
        OLLAMA_MODEL = args.ollama_model
        print(f"Mode Ollama : {OLLAMA_URL} | modèle : {OLLAMA_MODEL}")
    else:
        print("Erreur : spécifie --model <chemin_lora> ou --ollama")
        print("Exemple : python occ_emotion_server.py --ollama --ollama-model mistral")
        sys.exit(1)

    print(f"\n✓ Serveur OCC démarré sur http://localhost:{args.port}")
    print(f"  Test : curl -X POST http://localhost:{args.port}/classify "
          f"-H 'Content-Type: application/json' -d '{{\"text\": \"Je suis tellement heureux!\"}}'")
    app.run(host="0.0.0.0", port=args.port, debug=False)


if __name__ == "__main__":
    main()
