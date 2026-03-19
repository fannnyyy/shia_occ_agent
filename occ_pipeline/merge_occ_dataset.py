"""
Fusionne plusieurs fichiers occ_dataset.json en dédupliquant les textes.
Usage : python merge_datasets.py occ_dataset1.json occ_dataset2.json -o occ_dataset_final.json
"""
import argparse, json
from collections import Counter

p = argparse.ArgumentParser()
p.add_argument("files", nargs="+")
p.add_argument("-o", "--output", default="occ_dataset_final.json")
args = p.parse_args()

seen, merged = set(), []
for path in args.files:
    data = json.load(open(path, encoding="utf-8"))
    for ex in data:
        key = ex["texte"].strip().lower()
        if key not in seen:
            seen.add(key)
            merged.append(ex)

import random; random.shuffle(merged)
json.dump(merged, open(args.output, "w", encoding="utf-8"), ensure_ascii=False, indent=2)

counts = Counter(ex["emotion_occ"] for ex in merged)
print(f"Total : {len(merged)} exemples uniques ({args.output})")
print("\nDistribution :")
for emotion, n in sorted(counts.items()):
    print(f"  {emotion:<20} {n:>3}  {'█'*(n//2)}")