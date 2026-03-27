import win32com.client

print("=== Voix SAPI (anciennes) ===")
speaker = win32com.client.Dispatch("SAPI.SpVoice")
for i, voice in enumerate(speaker.GetVoices()):
    print(i, voice.GetDescription())

print("\n=== Voix OneCore (nouvelles) ===")
try:
    voices = win32com.client.Dispatch("MMSpeaker.SpVoice")
    for i, voice in enumerate(voices.GetVoices()):
        print(i, voice.GetDescription())
except:
    print("OneCore non accessible via COM")