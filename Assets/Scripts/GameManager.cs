using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using StudyDesign;
using System;


public class GameManager : MonoBehaviour
{
    public enum SCENE { PREP, PRACTICE, BEFORE_TRIAL, TRIAL, AFTER_TRIAL, BREAK, END, FINISHED }
    public SCENE current_scene = SCENE.PREP;
    public static GameManager instance;
    public TextMeshPro debugText;
    public TextMeshPro InfoText;
    // Start is called before the first frame update

    public ControlTargets targetControl;

    public CursorController cursorController;
    public StudyDesign.Study study;
    public Transform head;


    [SerializeField] private TrialData<HandCursorData> data_to_save_hand;
    [SerializeField] private TrialData<HeadCursorData> data_to_save_head;
    [SerializeField] private TrialData<EyeCursorData> data_to_save_eye;

    public float prep_progress = 0f;
    public float after_progress = 0f;
    public float break_progress = 0f;
    private float end_progress = 0.0f;
    const float TIME_AFTER = 10.0f;
    private const float bound = 15.0f;
    private float TIME_END = 3.0f;
    float TIME_BREAK = 3f;
    public int maxMsg = 5;
    private Queue<string> msg = new Queue<string>();


    //StudyDesign.FittsLaw fittsLaw;
    [SerializeField]
    public bool Tapped;
    public AudioSource audioSource_success;
    public AudioSource audioSource_fail;
    private float defaultDistance = 2.0f;
    private float MINIMUM_VELOCITY = 1.0f;

    [Header("Trial logging")]
    [SerializeField] float trialLogRateHz = 100f;
    float _trialLogAccum;
    int _trialLogSeq;

    float TrialLogIntervalSec => trialLogRateHz > 0f ? 1f / trialLogRateHz : 0f;
    public void makeSound(bool success)
    {
        if (success)
        {
            if (audioSource_success == null)
            {
                Debug.Log("no audio source detected");
            }
            else
            {
                audioSource_success.Play();
            }
        }
        else
        {
            audioSource_fail.Play();
        }
    }
    private void Awake()
    {
        instance = this;
    }
    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }
    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }
    private void Start()
    {

        //head = Microsoft.MixedReality.Toolkit.Utilities.CameraCache.Main.transform; //TODO

        Camera mainCamera = Camera.main;
        head = mainCamera.transform;
        targetControl = GameObject.Find("TargetPlane").GetComponent<ControlTargets>();
        current_scene = SCENE.PREP;

        study = new Study(_total_repetition: 1);
        Debug.Log("study" + study);
        //SceneChange(SCENE.TRIAL);
    }

    private void Update()
    {
        SceneUpdate();
        debugText.transform.localPosition = new Vector3(debugText.transform.localPosition.x, debugText.transform.localPosition.y, 2f);
        InfoText.transform.localPosition = new Vector3(InfoText.transform.localPosition.x, InfoText.transform.localPosition.y, 2f);
    }

    void HandleLog(string message, string stackTrace, LogType type)
    {
        if (msg.Count > maxMsg)
        {
            msg.Dequeue();
        }
        msg.Enqueue(message);
        debugText.text = FromQueueToString();
    }

    string FromQueueToString()
    {
        return string.Join("\n", msg.ToArray());
    }
    public void SceneChange(SCENE scene)
    {
        current_scene = scene;
        switch (scene)
        {

            case SCENE.PREP:
                break;
            case SCENE.BEFORE_TRIAL:
                if (study.fittsLaw.menu) study.SetStudyType();
                break;
            case SCENE.TRIAL:
                switch (study.currentCursor)
                {
                    case Study.CursorType.Eye:
                        data_to_save_eye = new TrialData<EyeCursorData>(study);
                        data_to_save_eye.log_sample_rate_hz = trialLogRateHz;
                        break;
                    case Study.CursorType.Head:
                        data_to_save_head = new TrialData<HeadCursorData>(study);
                        data_to_save_head.log_sample_rate_hz = trialLogRateHz;
                        break;
                    case Study.CursorType.Hand:
                        data_to_save_hand = new TrialData<HandCursorData>(study);
                        data_to_save_hand.log_sample_rate_hz = trialLogRateHz;
                        break;
                }
                _trialLogAccum = 0f;
                _trialLogSeq = 0;
                break;

            case SCENE.AFTER_TRIAL:
                switch (study.currentCursor)
                {
                    case Study.CursorType.Eye:
                        data_to_save_eye.SaveDataJson();
                        break;
                    case Study.CursorType.Head:
                        data_to_save_head.SaveDataJson();
                        break;
                    case Study.CursorType.Hand:
                        data_to_save_hand.SaveDataJson();
                        break;
                }
                after_progress = 0.0f;
                break;

            case SCENE.BREAK:
                break_progress = 0.0f;
                break;

            case SCENE.END:
                break;

            case SCENE.FINISHED:
                break;
        }
    }
    public void SceneUpdate()
    {
        switch (current_scene)
        {
            case SCENE.PREP:
                if (study == null)
                {
                    InfoText.text = "Initilizing System... ";
                    targetControl.ShowDwellTarget(false);
                }
                else
                {
                    InfoText.text = "Thank you for participating this study.\nPlease select the start button to begin\n" + "current condition is " + study.currentCursor + " :   " + study.currentSelection;
                    targetControl.ShowDwellTarget(true);
                }
                targetControl.ShowTargets(false);
                targetControl.ShowMenuTargets(false);
                prep_progress += Time.deltaTime;
                break;

            case SCENE.BEFORE_TRIAL:

                InfoText.text = "current condition is " + study.currentCursor + " : " + study.currentSelection;
                targetControl.ShowTargets(false);
                targetControl.ShowMenuTargets(false);
                targetControl.ShowDwellTarget(true);
                break;

            case SCENE.TRIAL:

                InfoText.text = "";

                if (study.fittsLaw.menu)
                {
                    targetControl.ShowTargets(false);
                    targetControl.ShowMenuTargets(true);
                }
                else
                {
                    targetControl.ShowTargets(true);
                    targetControl.ShowMenuTargets(false);
                }

                targetControl.ShowDwellTarget(false);

                study.fittsLaw.current_elapsed_time += Time.deltaTime;

                _trialLogAccum += Time.deltaTime;
                float logInterval = TrialLogIntervalSec;
                if (logInterval > 0f)
                {
                    int samplesDue = 0;
                    while (_trialLogAccum >= logInterval && samplesDue < 8)
                    {
                        RecordTrialFrame();
                        _trialLogAccum -= logInterval;
                        samplesDue++;
                    }
                }

                if (study.fittsLaw.check_timeout() && study.fittsLaw.onGoing == false)
                {
                    study.fittsLaw.nextStep(success: false);
                }
                break;

            case SCENE.AFTER_TRIAL:
                after_progress += Time.deltaTime;
                targetControl.ShowDwellTarget(false);
                targetControl.ShowTargets(false);
                targetControl.ShowMenuTargets(false);
                if (after_progress >= 3.0f)
                {
                    SceneChange(SCENE.BEFORE_TRIAL);
                }
                break;

            case SCENE.BREAK:
                InfoText.text = "This session was finished. \nPlease take off the headset and take a rest.\n" +
    "Break time remaining :" + (int)System.Math.Round(break_progress) + "/" + TIME_BREAK;


                targetControl.ShowTargets(false);
                targetControl.ShowDwellTarget(false);
                targetControl.ShowMenuTargets(false);

                break_progress += Time.deltaTime;
                if (break_progress >= TIME_BREAK)
                {
                    SceneChange(SCENE.BEFORE_TRIAL);
                }
                break;

            case SCENE.END:
                targetControl.ShowDwellTarget(false);
                targetControl.ShowMenuTargets(false);
                targetControl.ShowTargets(false);
                InfoText.text = "Whole study was finished. \nPlease take off the headset.\nThank you for participating the study.";
                end_progress += Time.deltaTime;
                if (end_progress >= TIME_END)
                {
                    SceneChange(SCENE.FINISHED);
                }
                break;

            case SCENE.FINISHED:
                break;
        }
    }

    void RecordTrialFrame()
    {
        float currentTime = Time.realtimeSinceStartup;
        long unixTimeMilliseconds = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        string formattedTime = DateTime.Now.ToString("yyyy MM dd HH mm ss fff");
        Transform endTargetTf = targetControl.getOneTarget(study.fittsLaw.endNum);
        Vector3 currentTargetPosition = endTargetTf.position;
        TargetBehaviour targetBehaviour = endTargetTf.GetComponent<TargetBehaviour>();
        float currentDwellTime = targetBehaviour != null ? targetBehaviour.currentDwellTime : 0f;

        switch (study.currentCursor)
        {
            case Study.CursorType.Eye:
            {
                var gp = cursorController.gazeProvider;
                Ray eyeRay = new Ray(cursorController.eyePosition, cursorController.eyeDirection);
                EyeCursorData currentEyeCursor = new EyeCursorData(
                    eyeRay,
                    neonGazeT: gp != null ? gp.LastNeonTimestampSec : double.NaN,
                    neonGazeTNs: gp != null ? gp.LastNeonTimestampNs : 0,
                    questGazeReceivedUnixMs: gp != null ? gp.LastQuestReceivedUnixMs : 0);
                float eye_angularDistance = Vector3.Angle((currentTargetPosition - cursorController.eyePosition), cursorController.eyeDirection);
                data_to_save_eye.Add(new FrameData<EyeCursorData>(
                    currentTime, unixTimeMilliseconds, formattedTime, study.fittsLaw, head, currentEyeCursor,
                    currentTargetPosition, eye_angularDistance, RayHitName(eyeRay), currentDwellTime, _trialLogSeq));
                break;
            }
            case Study.CursorType.Head:
            {
                Ray headRay = new Ray(cursorController.headPosition, cursorController.headDirection);
                HeadCursorData currentHeadCursor = new HeadCursorData(headRay);
                float head_angularDistance = Vector3.Angle((currentTargetPosition - currentHeadCursor.origin), currentHeadCursor.direction);
                data_to_save_head.Add(new FrameData<HeadCursorData>(
                    currentTime, unixTimeMilliseconds, formattedTime, study.fittsLaw, head, currentHeadCursor,
                    currentTargetPosition, head_angularDistance, RayHitName(headRay), currentDwellTime, _trialLogSeq));
                break;
            }
            case Study.CursorType.Hand:
            {
                HandCursorData currentHandCursor = new HandCursorData(cursorController.Handray);
                float hand_angularDistance = Vector3.Angle((currentTargetPosition - currentHandCursor.origin), currentHandCursor.direction);
                data_to_save_hand.Add(new FrameData<HandCursorData>(
                    currentTime, unixTimeMilliseconds, formattedTime, study.fittsLaw, head, currentHandCursor,
                    currentTargetPosition, hand_angularDistance, RayHitName(cursorController.Handray), currentDwellTime, _trialLogSeq));
                break;
            }
        }

        _trialLogSeq++;
    }

    static string RayHitName(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit))
            return hit.transform.name;
        return "None";
    }

    //public void SetInputMethod(string method)
    //{
    //    if (method.ToLower().Contains("head"))
    //    {
    //        inputMethod.SetHeadPointer();
    //    }
    //    else if (method.ToLower().Contains("eye"))
    //    {
    //        inputMethod.SetEyePointer();
    //    }
    //    else if (method.ToLower().Contains("hand"))
    //    {
    //        inputMethod.SetHandRayPointer();
    //    }
    //}
}



