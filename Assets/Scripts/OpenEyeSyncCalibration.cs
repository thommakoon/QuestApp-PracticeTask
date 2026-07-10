using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Phase 1 clock-sync calibration: send syncPulse ×3 to OpenEye during PREP, log Quest send times.
/// </summary>
public class OpenEyeSyncCalibration : MonoBehaviour
{
    [SerializeField] OpenEyeGazeReceiver receiver;
    [SerializeField] int pulseCount = 3;
    [SerializeField] float pulseIntervalSec = 0.3f;
    [SerializeField] float connectionTimeoutSec = 30f;

    bool _done;

    [Serializable]
    public class SyncPulseRecord
    {
        public int seq;
        public long quest_sent_unix_ms;
    }

    [Serializable]
    public class SyncPulseLog
    {
        public SyncPulseRecord[] pulses;
    }

    void Start()
    {
        if (receiver == null)
            receiver = GetComponent<OpenEyeGazeReceiver>();
        StartCoroutine(RunCalibration());
    }

    IEnumerator RunCalibration()
    {
        if (_done || receiver == null)
            yield break;

        float deadline = Time.realtimeSinceStartup + connectionTimeoutSec;
        while (receiver.CurrentState != OpenEyeGazeReceiver.State.Connected)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogWarning("[OpenEyeSync] timeout waiting for TCP connection");
                yield break;
            }
            yield return new WaitForSeconds(0.2f);
        }

        yield return new WaitForSeconds(0.5f);

        var records = new SyncPulseRecord[pulseCount];
        int sent = 0;
        for (int i = 0; i < pulseCount; i++)
        {
            if (receiver.TrySendSyncPulse(i, out long sentMs))
            {
                records[sent] = new SyncPulseRecord { seq = i, quest_sent_unix_ms = sentMs };
                sent++;
                Debug.Log($"[OpenEyeSync] pulse {i} quest_sent_unix_ms={sentMs}");
            }
            else
            {
                Debug.LogWarning($"[OpenEyeSync] pulse {i} send failed");
            }

            if (i + 1 < pulseCount)
                yield return new WaitForSeconds(pulseIntervalSec);
        }

        if (sent > 0)
            SaveLog(records, sent);

        _done = true;
    }

    void SaveLog(SyncPulseRecord[] records, int count)
    {
        var trimmed = new SyncPulseRecord[count];
        Array.Copy(records, trimmed, count);
        var log = new SyncPulseLog { pulses = trimmed };
        string json = JsonUtility.ToJson(log, true);
        string path = Path.Combine(Application.persistentDataPath, "sync_pulses_quest.json");
        File.WriteAllText(path, json);
        Debug.Log($"[OpenEyeSync] saved {path} ({count} pulses)");
    }
}
