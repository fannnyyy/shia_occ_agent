using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/*
 * ExperienceManager — orchestre le déroulement complet de l'expérience :
 * 1. Introduction par l'agent
 * 2. Conversation A (avec modèle OCC)
 * 3. Transition
 * 4. Conversation B (sans modèle OCC)
 * 5. QCM A puis QCM B
 */
public enum ExperiencePhase
{
    Introduction,
    ConversationA,
    Transition,
    ConversationB,
    QCM,
    Fin
}

public class ExperienceManager : MonoBehaviour
{
    [Header("Références")]
    public AvaturnLLMDialogManager dialogManager;

    [Header("UI")]
    public GameObject panelConversation;   // panel principal de la scène
    public GameObject panelTransition;     // panel de pause entre les deux conversations
    public GameObject panelQCM;     
    
    public Text textPhase;                 // texte affichant la phase en cours (optionnel)
    public Button btnSuivant;              // bouton "Suivant" sur les panels de transition/QCM

    [Header("Paramètres")]
    public int nombreEchangesParConversation = 5;

    // ── État interne ─────────────────────────────────────────────────────────
    private ExperiencePhase _phase = ExperiencePhase.Introduction;
    private int _echangesConvA = 0;
    private int _echangesConvB = 0;

    // ── Message d'introduction ───────────────────────────────────────────────
    private const string INTRO_MESSAGE =
        "Bonjour et bienvenue ! Je suis Mike, votre agent conversationnel. " +
        "Cette expérience se déroulera en deux parties. " +
        "Dans chaque partie, nous aurons une courte conversation de cinq échanges. " +
        "À la fin, un questionnaire vous sera proposé pour évaluer notre interaction. " +
        "Vous pouvez commencer en appuyant sur le bouton dictation.";

    private const string TRANSITION_MESSAGE =
        "Merci pour cette première conversation. " +
        "Nous allons maintenant faire une courte pause avant de commencer la deuxième partie.";

    private const string MESSAGE_QCM = 
    "Merci pour votre participation ! Veuillez maintenant remplir le questionnaire.";

    // ── Démarrage ────────────────────────────────────────────────────────────
    void Start()
    {
        // Désactive tous les panels sauf la conversation
        ShowPanel(ExperiencePhase.Introduction);
        btnSuivant.gameObject.SetActive(false);

        // Démarre l'introduction
        StartCoroutine(PlayIntroduction());
    }

    // ── Introduction ─────────────────────────────────────────────────────────
    IEnumerator PlayIntroduction()
    {
        _phase = ExperiencePhase.Introduction;
        UpdatePhaseText("Introduction");
        Debug.Log("[EXP] Phase : Introduction");

        // L'agent dit le message d'introduction via TTS
        dialogManager.InformationDisplay(INTRO_MESSAGE);
        dialogManager.PlayAudio(INTRO_MESSAGE);

        // Active le micro après l'intro (attendre ~5 secondes pour que le TTS finisse)
        yield return new WaitForSeconds(5f);

        PasserA(ExperiencePhase.ConversationA);
    }

    // ── Gestion des phases ───────────────────────────────────────────────────
    public void PasserA(ExperiencePhase nouvellePhase)
    {
        _phase = nouvellePhase;
        ShowPanel(nouvellePhase);
        UpdatePhaseText(nouvellePhase.ToString());
        Debug.Log("[EXP] Phase : " + nouvellePhase);

        switch (nouvellePhase)
        {
            case ExperiencePhase.ConversationA:
                _echangesConvA = 0;
                dialogManager.useOCC = true;    // active le modèle OCC
                dialogManager.accepteInput = true;
                Debug.Log("[EXP] Conversation A démarrée — modèle OCC activé");
                break;

            case ExperiencePhase.Transition:
                dialogManager.accepteInput = false;
                dialogManager.PlayAudio(TRANSITION_MESSAGE);
                Debug.Log("[EXP] Transition automatique dans 10 secondes...");
                StartCoroutine(TransitionAutomatique());
                break;

            case ExperiencePhase.ConversationB:
                btnSuivant.gameObject.SetActive(false);
                _echangesConvB = 0;
                dialogManager.useOCC = false;   // désactive le modèle OCC
                dialogManager.computationalModel.Reset(); 
                dialogManager.accepteInput = true;
                Debug.Log("[EXP] Conversation B démarrée — modèle OCC désactivé");
                break;

            case ExperiencePhase.QCM:
                dialogManager.accepteInput = false;  // désactive le micro
                dialogManager.PlayAudio(MESSAGE_QCM);
                Debug.Log("[EXP] Transition — en attente du bouton Suivant");
                break;


            case ExperiencePhase.Fin:
                dialogManager.accepteInput = false;
                dialogManager.InformationDisplay("Merci pour votre participation !");
                dialogManager.PlayAudio("Merci pour votre participation à cette expérience !");
                Debug.Log("[EXP] Expérience terminée");
                break;
        }
    }

    // ── Appelé par AvaturnLLMDialogManager après chaque réponse de l'agent ──
    public void OnEchangeComplete()
    {
        Debug.Log("[EXP] OnEchangeComplete reçu, phase actuelle : " + _phase);
    
        if (_phase == ExperiencePhase.ConversationA)
        {
            _echangesConvA++;
            Debug.Log($"[EXP] Échange A {_echangesConvA}/{nombreEchangesParConversation}");

            if (_echangesConvA >= nombreEchangesParConversation)
            {
                dialogManager.accepteInput = false; 
                Debug.Log("[EXP] Conversation A terminée → Transition");
                PasserA(ExperiencePhase.Transition);
                
            }
        }
        else if (_phase == ExperiencePhase.ConversationB)
        {
            _echangesConvB++;
            Debug.Log($"[EXP] Échange B {_echangesConvB}/{nombreEchangesParConversation}");

            if (_echangesConvB >= nombreEchangesParConversation)
            {
                dialogManager.accepteInput = false; 
                Debug.Log("[EXP] Conversation B terminée → QCM A");
                PasserA(ExperiencePhase.QCM);
            }
        }
    }

    // ── Bouton "Suivant" sur les panels ──────────────────────────────────────
    public void OnBoutonSuivant()
    {
        switch (_phase)
        {
            case ExperiencePhase.Transition:
                PasserA(ExperiencePhase.ConversationB);
                break;
            case ExperiencePhase.QCM:
                PasserA(ExperiencePhase.Fin);
                break;

        }
    }

    // ── Affichage des panels ─────────────────────────────────────────────────
    private void ShowPanel(ExperiencePhase phase)
    {


    }

    // ── Texte de phase (debug visuel) ────────────────────────────────────────
    private void UpdatePhaseText(string phase)
    {
        if (textPhase != null)
            textPhase.text = "Phase : " + phase;
    }

    // ── Accesseur de phase ───────────────────────────────────────────────────
    public ExperiencePhase GetPhase() => _phase;


    IEnumerator TransitionAutomatique()
    {
        yield return new WaitForSeconds(10f);
        PasserA(ExperiencePhase.ConversationB);
    }
}