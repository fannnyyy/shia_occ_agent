import requests

url = "https://beautiful-blackwell.mydocker-run-vd.centralesupelec.fr/api/chat/completions"
headers = {
    "Content-Type": "application/json",
    "Authorization": "Bearer sk-e7861181372349caada81f829cc90cc5",
}
payload = {
    "model": "gpt-oss-120b",
    "messages": [
        {"role": "user", "content": "Dis juste 'bonjour'"}
    ],
    "max_tokens": 50
}

resp = requests.post(url, headers=headers, json=payload)
print("Status code:", resp.status_code)
print("Réponse brute:", resp.text)