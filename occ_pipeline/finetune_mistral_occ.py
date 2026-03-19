"""
Étape 2 — Fine-tuning LoRA de Mistral-7B sur le dataset OCC.

Versions cibles :
    trl==0.8.6  transformers==4.40.0  peft==0.10.0

Prérequis :
    pip install "trl==0.8.6" "transformers==4.40.0" "peft==0.10.0"
    pip install datasets accelerate bitsandbytes torch

    GPU recommandé : 8 GB VRAM minimum (ex: RTX 3070+)

Utilisation :
    python finetune_mistral_occ.py --dataset occ_dataset.json
    python finetune_mistral_occ.py --dataset occ_dataset.json --epochs 5 --output my_model

Le modèle fine-tuné sera sauvegardé dans ./mistral-occ-lora/
"""

import argparse
import json

import torch
from datasets import Dataset
from peft import LoraConfig, TaskType, get_peft_model
from transformers import (
    AutoModelForCausalLM,
    AutoTokenizer,
    BitsAndBytesConfig,
    TrainingArguments,
)
from trl import SFTTrainer

# ── Émotions OCC ────────────────────────────────────────────────────────────
OCC_LABELS = [
    "JOY", "DISTRESS", "HOPE", "FEAR", "SATISFACTION", "DISAPPOINTMENT",
    "RELIEF", "FEARS_CONFIRMED", "HAPPY_FOR", "PITY", "RESENTMENT", "GLOATING",
    "PRIDE", "SHAME", "ADMIRATION", "REPROACH", "LOVE", "HATE",
    "GRATITUDE", "ANGER", "GRATIFICATION", "REMORSE",
]
LABELS_STR = ", ".join(OCC_LABELS)

INSTRUCTION_TEMPLATE = """Tu es un classificateur d'émotions OCC. Analyse le texte et retourne UNIQUEMENT le label OCC le plus approprié parmi : {labels}.

Texte : {texte}
Émotion OCC :"""


def load_dataset_from_json(path: str) -> Dataset:
    with open(path, encoding="utf-8") as f:
        data = json.load(f)

    data = [ex for ex in data if ex.get("emotion_occ") in OCC_LABELS]

    records = []
    for ex in data:
        prompt = INSTRUCTION_TEMPLATE.format(labels=LABELS_STR, texte=ex["texte"])
        full_text = prompt + " " + ex["emotion_occ"]
        records.append({"text": full_text})

    print(f"Dataset chargé : {len(records)} exemples valides")
    return Dataset.from_list(records)


def get_bnb_config() -> BitsAndBytesConfig:
    return BitsAndBytesConfig(
        load_in_4bit=True,
        bnb_4bit_compute_dtype=torch.float16,
        bnb_4bit_use_double_quant=True,
        bnb_4bit_quant_type="nf4",
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", default="occ_dataset.json")
    parser.add_argument("--base-model", default="mistralai/Mistral-7B-Instruct-v0.2")
    parser.add_argument("--output", default="mistral-occ-lora")
    parser.add_argument("--epochs", type=int, default=3)
    parser.add_argument("--batch-size", type=int, default=4)
    parser.add_argument("--lr", type=float, default=2e-4)
    parser.add_argument("--no-quantize", action="store_true")
    args = parser.parse_args()

    # ── Dataset ─────────────────────────────────────────────────────────────
    dataset = load_dataset_from_json(args.dataset)
    split = dataset.train_test_split(test_size=0.1, seed=42)
    train_dataset = split["train"]
    eval_dataset  = split["test"]
    print(f"Train : {len(train_dataset)} | Eval : {len(eval_dataset)}")

    # ── Tokenizer ───────────────────────────────────────────────────────────
    print(f"\nChargement du tokenizer : {args.base_model}")
    tokenizer = AutoTokenizer.from_pretrained(args.base_model, trust_remote_code=True)
    tokenizer.pad_token = tokenizer.eos_token
    tokenizer.padding_side = "right"

    # ── Modèle de base ──────────────────────────────────────────────────────
    print("Chargement du modèle de base...")
    model_kwargs = {
        "trust_remote_code": True,
        "torch_dtype": torch.float16,
    }

    has_gpu = torch.cuda.is_available()
    if has_gpu and not args.no_quantize:
        print("  → GPU détecté, quantisation 4-bit activée")
        model_kwargs["quantization_config"] = get_bnb_config()
        model_kwargs["device_map"] = {"": 0}
    elif has_gpu:
        print("  → GPU détecté, pas de quantisation")
        model_kwargs["device_map"] = "auto"
    else:
        print("  → Pas de GPU, entraînement CPU (lent)")
        model_kwargs["device_map"] = "cpu"

    model = AutoModelForCausalLM.from_pretrained(args.base_model, **model_kwargs)
    model.config.use_cache = False
    model.config.pretraining_tp = 1

    # ── LoRA ────────────────────────────────────────────────────────────────
    lora_config = LoraConfig(
        task_type=TaskType.CAUSAL_LM,
        r=16,
        lora_alpha=32,
        lora_dropout=0.05,
        target_modules=["q_proj", "v_proj", "k_proj", "o_proj",
                        "gate_proj", "up_proj", "down_proj"],
        bias="none",
    )
    model = get_peft_model(model, lora_config)
    model.print_trainable_parameters()

    # ── TrainingArguments ───────────────────────────────────────────────────
    training_args = TrainingArguments(
        output_dir=args.output,
        num_train_epochs=args.epochs,
        per_device_train_batch_size=args.batch_size,
        gradient_accumulation_steps=4,
        learning_rate=args.lr,
        fp16=has_gpu,
        logging_steps=10,
        evaluation_strategy="epoch",
        save_strategy="epoch",
        load_best_model_at_end=True,
        warmup_ratio=0.03,
        lr_scheduler_type="cosine",
        report_to="none",
    )

    # ── SFTTrainer (trl 0.8.6) ──────────────────────────────────────────────
    trainer = SFTTrainer(
        model=model,
        train_dataset=train_dataset,
        eval_dataset=eval_dataset,
        tokenizer=tokenizer,
        args=training_args,
        dataset_text_field="text",
        max_seq_length=256,
    )

    print("\n── Démarrage du fine-tuning ──")
    trainer.train()

    # ── Sauvegarde ──────────────────────────────────────────────────────────
    trainer.save_model(args.output)
    tokenizer.save_pretrained(args.output)
    print(f"\n✓ Modèle LoRA sauvegardé dans : {args.output}/")
    print(f"  Prochaine étape : python occ_emotion_server.py --model {args.output}")


if __name__ == "__main__":
    main()