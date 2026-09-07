using System;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEngine;
using Whisper;
using Whisper.Utils;

/// <summary>
/// Manages player voice input using Whisper.unity (Multilingual / Vietnamese support),
/// detects spoken spell keywords ('U', 'I', 'A' and Vietnamese tone variants), and syncs them over Netcode.
/// </summary>
public class PlayerVoiceController : NetworkBehaviour
{
    public static event Action<ulong, string> OnSpellCast;

    [Header("Voice Settings")]
    [SerializeField] private float _volumeThreshold = 0.003f; // Sensitive RMS volume threshold
    [SerializeField] private float _chunkDurationSec = 1.2f;  // Duration of each audio slice
    [SerializeField] private float _spellCooldown = 0.8f;
    [SerializeField] private string _modelPath = "Whisper/ggml-large-v3-q5_0.bin"; // Whisper Large v3

    private float _lastSpellTime;
    private WhisperManager _whisperManager;
    private MicrophoneRecord _micRecord;
    private bool _isTranscribing;

    public bool IsListening => _micRecord != null && _micRecord.IsRecording;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        SetupComponents();

        GameManager.OnGameStarted += StartListening;

        if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value == GameState.Playing)
            StartListening();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        GameManager.OnGameStarted -= StartListening;
        StopListening();
    }

    private void OnDestroy()
    {
        GameManager.OnGameStarted -= StartListening;
        StopListening();
    }

    private string GetBestAvailableModelPath()
    {
        string[] candidates = new string[]
        {
            _modelPath,
            "Whisper/ggml-large-v3-q5_0.bin",
            "Whisper/ggml-large-v3.bin",
            "Whisper/ggml-base.bin",
            "Whisper/ggml-tiny.bin"
        };

        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c)) continue;
            string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, c);
            if (System.IO.File.Exists(fullPath))
                return c;
        }

        return "Whisper/ggml-tiny.bin";
    }

    private async void SetupComponents()
    {
        string selectedModel = GetBestAvailableModelPath();

        // 1. Setup WhisperManager
        _whisperManager = FindFirstObjectByType<WhisperManager>();
        if (_whisperManager == null)
        {
            var go = new GameObject("WhisperManager");
            _whisperManager = go.AddComponent<WhisperManager>();
            _whisperManager.ModelPath = selectedModel;
            _whisperManager.language = "auto"; // Auto-detect Vietnamese & English
            _whisperManager.noContext = true;
            _whisperManager.initialPrompt = "U, I, A, Ú, Ù, Ả, Á, À, Đi, Nhảy, Bắn, You, Eye, Ah, Chạy.";
        }
        else
        {
            if (!_whisperManager.IsLoaded && !_whisperManager.IsLoading)
            {
                _whisperManager.ModelPath = selectedModel;
            }
            _whisperManager.language = "auto";
            _whisperManager.initialPrompt = "U, I, A, Ú, Ù, Ả, Á, À, Đi, Nhảy, Bắn, You, Eye, Ah, Chạy.";
        }

        if (!_whisperManager.IsLoaded && !_whisperManager.IsLoading)
        {
            await _whisperManager.InitModel();
        }

        // 2. Setup MicrophoneRecord
        _micRecord = GetComponent<MicrophoneRecord>();
        if (_micRecord == null)
        {
            _micRecord = gameObject.AddComponent<MicrophoneRecord>();
        }

        _micRecord.frequency = 16000;
        _micRecord.chunksLengthSec = _chunkDurationSec;
        _micRecord.maxLengthSec = 60;
        _micRecord.loop = true;
        _micRecord.echo = false;
        _micRecord.useVad = false;

        _micRecord.OnChunkReady -= OnAudioChunkReady;
        _micRecord.OnChunkReady += OnAudioChunkReady;
    }

    public void StartListening()
    {
        if (_micRecord == null)
            SetupComponents();

        if (_micRecord != null && !_micRecord.IsRecording)
        {
            _micRecord.StartRecord();
            Debug.Log("<color=green>[PlayerVoiceController] Microphone active (Hỗ trợ Tiếng Việt & English). Nói 'U', 'I', hoặc 'A'!</color>");
        }
    }

    public void StopListening()
    {
        if (_micRecord != null && _micRecord.IsRecording)
        {
            _micRecord.StopRecord();
        }
    }

    private async void OnAudioChunkReady(AudioChunk chunk)
    {
        if (_isTranscribing || _whisperManager == null || !_whisperManager.IsLoaded)
            return;

        float rms = CalculateRMS(chunk.Data);
        if (rms < _volumeThreshold)
            return;

        if (Time.time < _lastSpellTime + _spellCooldown)
            return;

        _isTranscribing = true;
        try
        {
            Debug.Log($"<color=white>[Mic Input] Nhận âm thanh (RMS: {rms:F4}), gửi sang Whisper...</color>");
            var res = await _whisperManager.GetTextAsync(chunk.Data, chunk.Frequency, chunk.Channels);
            
            if (res != null && !string.IsNullOrWhiteSpace(res.Result))
            {
                string raw = res.Result.Trim();
                Debug.Log($"<color=yellow>[Voice Heard]</color> \"{raw}\"");

                string detectedSpell = ParseSpellKeyword(raw);
                if (!string.IsNullOrEmpty(detectedSpell))
                {
                    _lastSpellTime = Time.time;
                    Debug.Log($"<color=cyan>[Voice Spell] ĐÃ NHẬN DIỆN '{detectedSpell}' từ \"{raw}\"!</color>");
                    CastSpellRpc(detectedSpell);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlayerVoiceController] Inference error: {e.Message}");
        }
        finally
        {
            _isTranscribing = false;
        }
    }

    private float CalculateRMS(float[] samples)
    {
        if (samples == null || samples.Length == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
            sum += samples[i] * samples[i];
        return Mathf.Sqrt(sum / samples.Length);
    }

    /// <summary>
    /// Parses spoken text to identify 'U', 'I', or 'A' keywords and their Vietnamese / English variants.
    /// </summary>
    public static string ParseSpellKeyword(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        // Clean punctuation but preserve Unicode/Vietnamese characters
        string cleaned = Regex.Replace(rawText.ToUpperInvariant(), @"[^\w\s\u00C0-\u1EF9]", " ").Trim();
        string[] words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // 1. Check each word
        foreach (var w in words)
        {
            // Match 'U' / 'Ú' / 'Ù' / 'Ủ' / 'Ũ' / 'Ụ' / 'YOU' / 'OO' / 'YOO' / 'Ư'
            if (w == "U" || w == "Ú" || w == "Ù" || w == "Ủ" || w == "Ũ" || w == "Ụ" ||
                w == "Ư" || w == "Ứ" || w == "Ừ" || w == "Ử" || w == "Ữ" || w == "Ự" ||
                w == "YOU" || w == "OO" || w == "YOO" || w == "YU" || w == "WHO")
                return "U";

            // Match 'I' / 'Í' / 'Ì' / 'Ỉ' / 'Ĩ' / 'Ị' / 'Y' / 'Ý' / 'Ỳ' / 'Ỷ' / 'Ỹ' / 'Ỵ' / 'EYE' / 'AI'
            if (w == "I" || w == "Í" || w == "Ì" || w == "Ỉ" || w == "Ĩ" || w == "Ị" ||
                w == "Y" || w == "Ý" || w == "Ỳ" || w == "Ỷ" || w == "Ỹ" || w == "Ỵ" ||
                w == "EYE" || w == "AI" || w == "AYE" || w == "HIGH" || w == "ICE")
                return "I";

            // Match 'A' / 'Á' / 'À' / 'Ả' / 'Ã' / 'Ạ' / 'Ă' / 'Â' / 'AH' / 'AY' / 'EH'
            if (w == "A" || w == "Á" || w == "À" || w == "Ả" || w == "Ã" || w == "Ạ" ||
                w == "Ă" || w == "Ắ" || w == "Ằ" || w == "Ẳ" || w == "Ẵ" || w == "Ặ" ||
                w == "Â" || w == "Ấ" || w == "Ầ" || w == "Ẩ" || w == "Ẫ" || w == "Ậ" ||
                w == "AH" || w == "AY" || w == "EH" || w == "EI" || w == "HEY" || w == "HAY" || w == "A DI")
                return "A";
        }

        // 2. Fallback check for single characters or containing keywords
        if (cleaned.Contains("YOU") || cleaned.Contains(" U ") || cleaned.StartsWith("U ") || cleaned.EndsWith(" U") || cleaned == "U" || cleaned.Contains("Ú") || cleaned.Contains("Ù"))
            return "U";
        if (cleaned.Contains("EYE") || cleaned.Contains(" I ") || cleaned.StartsWith("I ") || cleaned.EndsWith(" I") || cleaned == "I" || cleaned.Contains("Í") || cleaned.Contains("Ì"))
            return "I";
        if (cleaned.Contains("AH") || cleaned.Contains(" A ") || cleaned.StartsWith("A ") || cleaned.EndsWith(" A") || cleaned == "A" || cleaned.Contains("Á") || cleaned.Contains("À"))
            return "A";

        return null;
    }

    [Rpc(SendTo.Server)]
    private void CastSpellRpc(string spellType)
    {
        if (string.IsNullOrEmpty(spellType)) return;

        Debug.Log($"[Server] Player {OwnerClientId} cast spell '{spellType}'");
        BroadcastSpellRpc(OwnerClientId, spellType);
    }

    [Rpc(SendTo.Everyone)]
    private void BroadcastSpellRpc(ulong senderClientId, string spellType)
    {
        OnSpellCast?.Invoke(senderClientId, spellType);
        ExecuteSpellEffect(senderClientId, spellType);
    }

    private void ExecuteSpellEffect(ulong senderClientId, string spellType)
    {
        Debug.Log($"<color=yellow>[Spell Effect] Player {senderClientId} triggered spell '{spellType}'!</color>");

        if (senderClientId == OwnerClientId)
        {
            switch (spellType)
            {
                case "U":
                    Debug.Log("<color=cyan>[Action U] Tăng tốc độ / Lướt nhanh kích hoạt!</color>");
                    break;
                case "I":
                    Debug.Log("<color=magenta>[Action I] Khiên bảo vệ kích hoạt!</color>");
                    break;
                case "A":
                    Debug.Log("<color=red>[Action A] Nhảy cao / Sóng xung kích kích hoạt!</color>");
                    break;
            }
        }
    }
}
