import requests

# Test 1 : endpoint OpenWebUI natif
url1 = "https://beautiful-blackwell.mydocker-run-vd.centralesupelec.fr/api/chat/completions"

# Test 2 : endpoint OpenAI compatible
url2 = "https://beautiful-blackwell.mydocker-run-vd.centralesupelec.fr/v1/chat/completions"

# Test 3 : liste des modèles
url3 = "https://beautiful-blackwell.mydocker-run-vd.centralesupelec.fr/api/models"

headers = {
    "Content-Type": "application/json",
    "Authorization": "Bearer sk-e7861181372349caada81f829cc90cc5",
}
payload = {
    "model": "gpt-oss-120b",
    "messages": [{"role": "user", "content": "bonjour"}],
    "stream": False
}

print("=== Test 1 /api/chat/completions ===")
r = requests.post(url1, headers=headers, json=payload)
print(r.status_code, r.text[:200])

print("\n=== Test 2 /v1/chat/completions ===")
r = requests.post(url2, headers=headers, json=payload)
print(r.status_code, r.text[:200])

print("\n=== Test 3 /api/models (GET) ===")
r = requests.get(url3, headers=headers)
print(r.status_code, r.text[:200])