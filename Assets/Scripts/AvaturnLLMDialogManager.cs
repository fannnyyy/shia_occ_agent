using ACTA;
using Assets.Scripts;
using Assets.Scripts.Utils;
using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Windows.Speech;
using Whisper;
using Whisper.Utils;
using Application = UnityEngine.Application;
using Button = UnityEngine.UI.Button;
using Debug = UnityEngine.Debug;
using Text = UnityEngine.UI.Text;


public enum EndPoint
{
    OpenWebUI,
    Ollama
};

public enum OccEmotion
{
    JOY, DISTRESS, HOPE, FEAR,
    SATISFACTION, DISAPPOINTMENT, RELIEF, FEARS_CONFIRMED,
    HAPPY_FOR, PITY, RESENTMENT, GLOATING,
    PRIDE, SHAME, ADMIRATION, REPROACH,
    LOVE, HATE,
    GRATITUDE, ANGER, GRATIFICATION, REMORSE,
}

/// <summary>
/// Phases du protocole expérimental.
/// Intro → ConvA (OCC actif) → Transition (reset) → ConvB (OCC inactif) → End
/// </summary>
public enum ExperimentPhase { Intro, ConvA, Transition, ConvB, End }

/*
 * AvaturnLLMDialogManager — gestion conversationnelle + protocole expérimental.
 *
 * Protocole :
 *   1. Introduction  : agent parle seul (bouton désactivé)
 *   2. Conversation A : 5 répliques participant, modèle OCC actif
 *   3. Transition    : message neutre, reset mémoire + émotions
 *   4. Conversation B : 5 répliques participant, OCC désactivé
 *   5. Fin           : agent oriente vers le formulaire
 *
 * La transition se déclenche à la FIN de la 5e réplique du participant (ConvA),
 * sans appel LLM supplémentaire.
 */
public class AvaturnLLMDialogManager : MonoBehaviour
{

    public AudioSource audioSource;
    public float volume = 0.5f;

    public Transform informationPanel;
    public Transform textPanel;
    public Transform buttonPanel;
    public GameObject ButtonPrefab;
    private GameObject button;
    public FacialExpressionAvaturn faceExpression;
    private Animator anim;

    // dictation
    private DictationRecognizer dictationRecognizer;

    // whisper
    public bool useWhisper = true;
    public WhisperManager whisper;
    public MicrophoneRecord microphoneRecord;
    public bool streamSegments = true;
    public bool printLanguage = false;
    private string _buffer;

    // conversation memory
    public int numberOfTurn = 10;
    private JsonParser jsonParser = new JsonParser();
    private JsonValue conversationList = new JsonValue(JsonType.Array);

    private GenerateConversationJSON _conv;

    // LLM
    public string urlOllama;
    public EndPoint endPoint = EndPoint.OpenWebUI;
    public string modelName;
    public string APIkey;
    [TextArea(15, 20)]
    public string preprompt;
    private string _response;

    private string _lastUserText = "";
    private string _lastEmotion = "";
    private string _lastHint = "";
    private string _lastPreprompt = "";

    // piper
    public bool usePiper = true;
    public int piperPort = 5000;
    public float speakerID = 1;
    public bool usePhonemeGenerator = false;

    // ComputationalModel
    public ComputationalModelOCC computationalModel;

    [Header("Serveur OCC")]
    public string occServerUrl = "http://localhost:5050";

    // ── Protocole expérimental ────────────────────────────────────────────
    [Header("Protocole expérimental")]
    public ExperimentPhase currentPhase = ExperimentPhase.Intro;

    [Tooltip("Nombre de répliques participant par conversation (A et B)")]
    public int maxTurnsPerPhase = 5;

    [TextArea(3, 5)]
    public string introText =
        "Bonjour ! Je suis votre assistant pour cette expérience. " +
        "Nous allons avoir deux courtes conversations. " +
        "Parlez-moi librement, je suis là pour vous écouter.";

    [TextArea(3, 5)]
    public string transitionText =
        "Merci pour cette première conversation. " +
        "Prenons une courte pause avant de commencer la deuxième partie.";

    [TextArea(3, 5)]
    public string endText =
        "Merci beaucoup pour votre participation. " +
        "Vous pouvez maintenant remplir le formulaire.";

    private int _userTurnCount = 0;
    private bool _occActive = true;

    // ── Start ─────────────────────────────────────────────────────────────
    void Start()
    {
        computationalModel = gameObject.GetComponent<ComputationalModelOCC>();
        if (computationalModel == null)
            computationalModel = gameObject.AddComponent<ComputationalModelOCC>();

        anim = this.gameObject.GetComponent<Animator>();
        InformationDisplay("");
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = "";

        button = (GameObject)Instantiate(ButtonPrefab);
        button.GetComponentInChildren<Text>().text = "Dictation";
        button.GetComponent<Button>().onClick.AddListener(delegate { OnButtonPressed(); });
        button.GetComponent<RectTransform>().position = new Vector3(0 * 170.0f + 90.0f, 39.0f, 0.0f);
        button.transform.SetParent(buttonPanel);

        // dictation
        dictationRecognizer = new DictationRecognizer();
        dictationRecognizer.AutoSilenceTimeoutSeconds = 10;
        dictationRecognizer.InitialSilenceTimeoutSeconds = 10;
        dictationRecognizer.DictationResult += DictationRecognizer_DictationResult;
        dictationRecognizer.DictationError += DictationRecognizer_DictationError;
        dictationRecognizer.DictationComplete += DictationRecognizer_DictationComplete;

        // whisper
        whisper.OnNewSegment += OnNewSegment;
        microphoneRecord.OnRecordStop += OnRecordStop;

        _conv = gameObject.AddComponent<GenerateConversationJSON>();

        // Lancement automatique de l'introduction
        StartCoroutine(RunIntro());
    }

    // ── Phases du protocole ───────────────────────────────────────────────

    IEnumerator RunIntro()
    {
        currentPhase = ExperimentPhase.Intro;
        button.GetComponent<Button>().interactable = false;
        InformationDisplay(introText);
        PlayAudio(introText);
        // Attend que l'audio se termine
        yield return new WaitForSeconds(1f);
        yield return new WaitUntil(() => !audioSource.isPlaying);

        currentPhase = ExperimentPhase.ConvA;
        _userTurnCount = 0;
        _occActive = true;
        button.GetComponent<Button>().interactable = true;
        InformationDisplay("— Conversation A — (modèle émotionnel actif)");
        Debug.Log("[PROTOCOLE] Phase : ConvA — OCC actif");
    }

    IEnumerator RunTransition()
    {
        currentPhase = ExperimentPhase.Transition;
        button.GetComponent<Button>().interactable = false;

        // Reset mémoire conversationnelle + émotions OCC
        conversationList.ArrayValues.Clear();
        computationalModel.ResetEmotions();
        _lastEmotion = "";
        _lastHint = "";

        InformationDisplay(transitionText);
        PlayAudio(transitionText);
        yield return new WaitForSeconds(1f);
        yield return new WaitUntil(() => !audioSource.isPlaying);

        currentPhase = ExperimentPhase.ConvB;
        _userTurnCount = 0;
        _occActive = false;
        button.GetComponent<Button>().interactable = true;
        InformationDisplay("— Conversation B — (modèle émotionnel désactivé)");
        Debug.Log("[PROTOCOLE] Phase : ConvB — OCC inactif");
    }

    IEnumerator RunEnd()
    {
        currentPhase = ExperimentPhase.End;
        button.GetComponent<Button>().interactable = false;
        InformationDisplay(endText);
        PlayAudio(endText);
        yield return new WaitForSeconds(1f);
        yield return new WaitUntil(() => !audioSource.isPlaying);
        Debug.Log("[PROTOCOLE] Expérience terminée.");
    }

    // ── Entrée utilisateur (point d'entrée unique) ────────────────────────

    private void HandleUserInput(string text)
    {
        if (currentPhase != ExperimentPhase.ConvA && currentPhase != ExperimentPhase.ConvB)
            return;

        _lastUserText = text;

        JsonValue userTurn = new JsonValue(JsonType.Object);
        JsonValue userRole = new JsonValue(JsonType.String);
        userRole.StringValue = "user";
        JsonValue userContent = new JsonValue(JsonType.String);
        userContent.StringValue = text;
        userTurn.ObjectValues.Add("role", userRole);
        userTurn.ObjectValues.Add("content", userContent);
        conversationList.ArrayValues.Add(userTurn);
        if (conversationList.ArrayValues.Count > numberOfTurn)
            conversationList.ArrayValues.RemoveAt(0);

        _userTurnCount++;

        // 5e réplique ConvA → transition immédiate (pas d'appel LLM)
        if (currentPhase == ExperimentPhase.ConvA && _userTurnCount >= maxTurnsPerPhase)
        {
            StartCoroutine(RunTransition());
            return;
        }

        // 5e réplique ConvB → fin de l'expérience (pas d'appel LLM)
        if (currentPhase == ExperimentPhase.ConvB && _userTurnCount >= maxTurnsPerPhase)
        {
            StartCoroutine(RunEnd());
            return;
        }

        // Appel LLM normal — OCC actif ou non selon la phase
        if (_occActive)
            StartCoroutine(ClassifyThenChat(text, conversationList));
        else
            SendToChat(conversationList);
    }

    // ── Dictation ────────────────────────────────────────────────────────

    private void DictationRecognizer_DictationComplete(DictationCompletionCause cause)
    {
        button.GetComponentInChildren<Text>().text = "Dictation";
    }

    private void DictationRecognizer_DictationError(string error, int hresult)
    {
        useWhisper = true;
        button.GetComponentInChildren<Text>().text = "Record";
    }

    private void DictationRecognizer_DictationResult(string text, ConfidenceLevel confidence)
    {
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = text;
        HandleUserInput(text);
    }

    // ── Whisper ──────────────────────────────────────────────────────────

    private async void OnRecordStop(AudioChunk audioChunk)
    {
        _buffer = "";
        var res = await whisper.GetTextAsync(audioChunk.Data, audioChunk.Frequency, audioChunk.Channels);
        if (res == null)
            return;

        var text = res.Result;
        if (printLanguage)
            text += $"\n\nLanguage: {res.Language}";

        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = text;
        HandleUserInput(text);
    }

    private void OnButtonPressed()
    {
        if (useWhisper)
        {
            if (!microphoneRecord.IsRecording)
            {
                microphoneRecord.StartRecord();
                button.GetComponentInChildren<Text>().text = "Stop";
            }
            else
            {
                microphoneRecord.StopRecord();
                button.GetComponentInChildren<Text>().text = "Record";
            }
        }
        else
        {
            if (dictationRecognizer.Status != SpeechSystemStatus.Running)
            {
                dictationRecognizer.Start();
                button.GetComponentInChildren<Text>().text = "Stop";
            }
            if (dictationRecognizer.Status == SpeechSystemStatus.Running)
            {
                dictationRecognizer.Stop();
                button.GetComponentInChildren<Text>().text = "Dictation";
            }
        }
    }

    private void OnNewSegment(WhisperSegment segment)
    {
        if (!streamSegments)
            return;
        _buffer += segment.Text;
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = _buffer + "...";
    }

    void Update() { }

    // ── LLM ──────────────────────────────────────────────────────────────

    IEnumerator ClassifyThenChat(string userText, JsonValue conversation)
    {
        Debug.Log("[OCC] Classification en cours...");
        yield return StartCoroutine(ClassifyOCC(userText));
        Debug.Log("[OCC] Classification terminée, envoi au LLM");
        SendToChat(conversation);
    }

    IEnumerator ChatRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");
        uwr.SetRequestHeader("Authorization", "Bearer " + APIkey);

        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            Debug.Log("Error While Sending: " + uwr.error);
            Debug.Log("Response Code: " + uwr.responseCode);
            Debug.Log("Response Body: " + uwr.downloadHandler.text);
        }
        else
        {
            _response = uwr.downloadHandler.text;
            JsonValue response = jsonParser.Parse(_response);
            String responseString = "";
            if (endPoint == EndPoint.OpenWebUI)
                responseString = response.ObjectValues["choices"].ArrayValues[0].ObjectValues["message"].ObjectValues["content"].StringValue;
            else if (endPoint == EndPoint.Ollama)
                responseString = response.ObjectValues["message"].ObjectValues["content"].StringValue;

            InformationDisplay(responseString);
            _response = responseString;

            JsonValue assistantTurn = new JsonValue(JsonType.Object);
            JsonValue assistantRole = new JsonValue(JsonType.String);
            assistantRole.StringValue = "assistant";
            JsonValue assistantContent = new JsonValue(JsonType.String);
            assistantContent.StringValue = _response;
            assistantTurn.ObjectValues.Add("role", assistantRole);
            assistantTurn.ObjectValues.Add("content", assistantContent);
            conversationList.ArrayValues.Add(assistantTurn);
            if (conversationList.ArrayValues.Count > numberOfTurn)
                conversationList.ArrayValues.RemoveAt(0);

            PlayAudio(_response);

            if (_conv != null)
                _conv.LogTurn(
                    _lastHint,
                    _lastPreprompt,
                    _lastUserText,
                    responseString,
                    _lastEmotion,
                    computationalModel.GetCurrentEmotion().ToString(),
                    currentPhase.ToString()
                );
        }
    }

    IEnumerator UserRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");
        uwr.SetRequestHeader("Authorization", "Bearer " + APIkey);

        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
            Debug.Log("Error While Sending: " + uwr.error);
        else
        {
            _response = uwr.downloadHandler.text;
            JsonValue response = jsonParser.Parse(_response);
            computationalModel.UserValues(response.StringValue);
        }
    }

    IEnumerator LLMRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");
        uwr.SetRequestHeader("Authorization", "Bearer " + APIkey);

        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
            Debug.Log("Error While Sending: " + uwr.error);
        else
        {
            _response = uwr.downloadHandler.text;
            JsonValue response = jsonParser.Parse(_response);
            computationalModel.LLMValues(response.StringValue);
        }
    }

    private void SendToChat(JsonValue conversationList)
    {
        if (conversationList.ArrayValues.Count == 0)
            return;

        JsonValue fullConv = new JsonValue(JsonType.Array);
        JsonValue systemTurn = new JsonValue(JsonType.Object);
        JsonValue systemRole = new JsonValue(JsonType.String);
        systemRole.StringValue = "system";
        JsonValue systemContent = new JsonValue(JsonType.String);

        // OCC inactif en ConvB : pas d'indice émotionnel injecté dans le preprompt
        string emotionalHint = (_occActive && computationalModel != null)
            ? computationalModel.GetPersonalityHint()
            : "neutral and balanced";
        string fullPreprompt = preprompt + " You are currently feeling : " + emotionalHint + ".";

        _lastHint = emotionalHint;
        _lastPreprompt = fullPreprompt;

        Debug.Log("[PREPROMPT] Émotion dominante: " +
            (_occActive ? computationalModel.GetCurrentEmotion().ToString() : "N/A (OCC off)") +
            " | Hint: " + emotionalHint);

        systemContent.StringValue = Regex.Replace(Regex.Replace(fullPreprompt, "[\"\']", ""), "\\s", " ");
        systemTurn.ObjectValues.Add("role", systemRole);
        systemTurn.ObjectValues.Add("content", systemContent);
        fullConv.ArrayValues.Add(systemTurn);
        fullConv.ArrayValues.AddRange(conversationList.ArrayValues);

        JsonValue data = new JsonValue(JsonType.Object);
        JsonValue modelNameValue = new JsonValue(JsonType.String);
        modelNameValue.StringValue = modelName;
        data.ObjectValues.Add("model", modelNameValue);
        data.ObjectValues.Add("messages", fullConv);
        JsonValue streamValue = new JsonValue(JsonType.Boolean);
        streamValue.BoolValue = false;
        data.ObjectValues.Add("stream", streamValue);

        string endPointS = endPoint == EndPoint.OpenWebUI ? "api/chat/completions" : "api/chat";
        StartCoroutine(ChatRequest(urlOllama + endPointS, data.ToJsonString()));
    }

    IEnumerator ClassifyOCC(string text)
    {
        string json = "{\"text\": \"" + text.Replace("\"", "\\\"").Replace("\n", " ") + "\"}";
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest req = new UnityWebRequest(occServerUrl + "/classify", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[OCC] Serveur inaccessible : " + req.error);
                yield break;
            }

            JsonValue response = jsonParser.Parse(req.downloadHandler.text);
            string emotion = response.ObjectValues["emotion"].StringValue;
            float confidence = (float)response.ObjectValues["confidence"].NumberValue;

            Debug.Log("[OCC] Émotion détectée : " + emotion + " (" + confidence + ")");

            if (Enum.TryParse(emotion, out OccEmotion occEmotion))
            {
                computationalModel.UpdateEmotion(occEmotion, confidence);
                _lastEmotion = emotion;
            }
        }
    }

    // ── Audio ─────────────────────────────────────────────────────────────

    public void PlayAudio(int a)
    {
        try
        {
            AudioClip music = (AudioClip)Resources.Load("Sounds/" + a);
            audioSource.PlayOneShot(music, volume);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogException(e);
        }
    }

    IEnumerator postTTSRequest(string text)
    {
        text = Regex.Replace(Regex.Replace(text, "[\"\']", ""), "\\s", " ");
        var uwr = new UnityWebRequest("http://localhost:" + piperPort.ToString(), "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(
            "{ \"text\": \"" + text + "\" , \"speaker_id\": " + speakerID.ToString() + "}");
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");

        yield return uwr.SendWebRequest();
        byte[] wavData = uwr.downloadHandler.data;
        if (usePhonemeGenerator)
        {
            string json = Wav2VecClient.SendWav(wavData);
            Debug.Log("Python returned: " + json);
        }

        AudioClip clip = WavUtility.ToAudioClip(wavData, "DownloadedClip");
        audioSource.clip = clip;
        audioSource.Play();
    }

    public void PlayAudio(string text)
    {
        if (!usePiper)
        {
#if UNITY_STANDALONE_WIN
            Narrator.speak(text);
#else
            Debug.Log("Narrator not available");
#endif
        }
        else
        {
            StartCoroutine(postTTSRequest(text));
        }
    }

    // ── UI ────────────────────────────────────────────────────────────────

    public void InformationDisplay(string s)
    {
        Text text = informationPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        text.text = s;
    }

    public void DisplayQuestion(string s)
    {
        Text text = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        text.text = s;
    }

    public void EndDialog()
    {
        anim.SetTrigger("Greet");
    }

    public void DisplayAUs(int[] aus, int[] intensities, float duration)
    {
        faceExpression.setFacialAUs(aus, intensities, duration);
    }

    public void Doubt(float intensity_factor, float duration)
    {
        DisplayAUs(new int[] { 6, 4, 14 },
            new int[] { (int)(intensity_factor * 100), (int)(intensity_factor * 80), (int)(intensity_factor * 80) },
            duration);
    }
}
