using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Eitan.SherpaONNXUnity.Runtime.Native;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Production-Grade Hybrid Voice Controller for Unity Multiplayer:
/// 1. Frontend DSP: Pre-emphasis filter + Zero Crossing Rate (ZCR) breath/noise gating.
/// 2. Dual-Engine Verification:
///    - Branch A: LPC Formant Tracking (Physical F1 & F2 vowel resonance extraction).
///    - Branch B: Sherpa-ONNX Zipformer Vietnamese neural token decoding.
/// 3. Consensus Gate: Cross-validates acoustic formant fingerprint with neural ASR.
/// 4. Duration Classifier: Accurately distinguishes short burst ("U", "I", "A") vs sustained ("U_LONG", "I_LONG", "A_LONG").
/// </summary>
public class SherpaVoiceController : NetworkBehaviour
{
    public static event Action<ulong, string> OnSpellCast;

    [Header("Sherpa-ONNX Model Settings")]
    [SerializeField] private string _modelFolder = "SherpaOnnx/sherpa-onnx-zipformer-vi-30M-int8-2026-02-09";
    [SerializeField] private string _encoderFile = "encoder.int8.onnx";
    [SerializeField] private string _decoderFile = "decoder.onnx";
    [SerializeField] private string _joinerFile = "joiner.int8.onnx";
    [SerializeField] private string _tokensFile = "tokens.txt";

    [Header("Audio & Noise Gating (DSP)")]
    [SerializeField] private float _volumeThreshold = 0.0035f;
    [SerializeField] private float _maxZcrThreshold = 0.28f;       // Filters out breath/fricatives/clicks (ZCR > 0.28)
    [SerializeField] private float _minVowelDuration = 0.18f;      // Discard clicks/pops shorter than 0.18s
    [SerializeField] private float _sustainedThreshold = 0.65f;    // >= 0.65s continuous vowel triggers Charged Spell (Ngân dài)
    [SerializeField] private float _spellCooldown = 0.5f;
    [SerializeField] private int _sampleRate = 16000;

    [Header("Multiplayer Voice Tracking")]
    [SerializeField, Min(0.05f)] private float _heartbeatInterval = 0.15f;
    [SerializeField, Range(3, 9)] private int _vowelWindowSize = 5;
    [SerializeField, Range(2, 7)] private int _minimumStableVotes = 3;

    public NetworkVariable<VoiceTrackState> TrackedState = new(
        VoiceTrackState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<VoiceVowel> TrackedVowel = new(
        VoiceVowel.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<float> TrackedContinuousDuration = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<VoiceEndReason> TrackedEndReason = new(
        VoiceEndReason.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsActivelyVoicing = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private OfflineRecognizer _recognizer;
    private float _lastSpellTime;
    private AudioClip _micClip;
    private string _selectedDevice;
    private int _lastSamplePos;
    private bool _isRecording;
    private bool _isProcessing;

    // Sustained utterance buffer
    private readonly List<float> _currentUtteranceSamples = new List<float>();
    private float _currentVoiceDuration;
    private float _silenceDuration;
    private const float SILENCE_TIMEOUT = 0.18f; // If silence persists > 0.18s, utterance has ended
    private readonly Queue<VoiceVowel> _recentVowelVotes = new Queue<VoiceVowel>();
    private VoiceVowel _stableVowel;
    private uint _utteranceSequence;
    private bool _isTrackingUtterance;
    private float _nextHeartbeatTime;

    public bool IsListening => _isRecording;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        InitRecognizer();

        // Observer Pattern: Subscribe to GameManager's OnGameStarted event
        GameManager.OnGameStarted += StartListening;

        // If spawned while already playing, start immediately
        if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value == GameState.Playing)
            StartListening();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        GameManager.OnGameStarted -= StartListening;
        StopListening();
        DisposeRecognizer();
    }

    public override void OnDestroy()
    {
        GameManager.OnGameStarted -= StartListening;
        StopListening();
        DisposeRecognizer();
        base.OnDestroy();
    }

    private void InitRecognizer()
    {
        try
        {
            string fullModelPath = Path.Combine(Application.streamingAssetsPath, _modelFolder);
            string encoderPath = Path.Combine(fullModelPath, _encoderFile);
            string decoderPath = Path.Combine(fullModelPath, _decoderFile);
            string joinerPath = Path.Combine(fullModelPath, _joinerFile);
            string tokensPath = Path.Combine(fullModelPath, _tokensFile);

            if (!File.Exists(encoderPath) || !File.Exists(tokensPath))
            {
                Debug.LogError($"[SherpaVoiceController] Không tìm thấy file model trong: {fullModelPath}");
                return;
            }

            var config = new OfflineRecognizerConfig(true);
            config.FeatConfig.SampleRate = _sampleRate;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Transducer.Encoder = encoderPath;
            config.ModelConfig.Transducer.Decoder = decoderPath;
            config.ModelConfig.Transducer.Joiner = joinerPath;
            config.ModelConfig.Tokens = tokensPath;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.NumThreads = 2;
            config.DecodingMethod = "greedy_search";
            config.HotwordsFile = "";

            _recognizer = new OfflineRecognizer(config);
            Debug.Log("<color=green>[SherpaVoiceController] Khởi tạo thành công Hybrid Pipeline: DSP Formants + Sherpa-ONNX Zipformer VN!</color>");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SherpaVoiceController] Failed to init Sherpa-ONNX: {ex.Message}");
        }
    }

    private void DisposeRecognizer()
    {
        if (_recognizer != null)
        {
            _recognizer.Dispose();
            _recognizer = null;
        }
    }

    public void StartListening()
    {
        if (_isRecording) return;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[SherpaVoiceController] Không tìm thấy thiết bị Microphone!");
            return;
        }

        if (_recognizer == null)
            InitRecognizer();

        _selectedDevice = Microphone.devices[0];
        _micClip = Microphone.Start(_selectedDevice, true, 20, _sampleRate);
        _lastSamplePos = 0;
        _isRecording = true;
        _currentUtteranceSamples.Clear();
        _currentVoiceDuration = 0f;
        _silenceDuration = 0f;
        _isTrackingUtterance = false;
        _stableVowel = VoiceVowel.None;
        _recentVowelVotes.Clear();

        Debug.Log("<color=green>[SherpaVoiceController] Microphone active! Pipeline Sẵn Sàng (U, I, A - Âm ngắn & Ngân dài)...</color>");
    }

    public void StopListening()
    {
        if (!_isRecording) return;

        if (!string.IsNullOrEmpty(_selectedDevice) && Microphone.IsRecording(_selectedDevice))
        {
            Microphone.End(_selectedDevice);
        }

        if (_isTrackingUtterance && IsSpawned && IsOwner)
            EndVoiceTrackingRpc(
                _utteranceSequence,
                _stableVowel,
                VoiceEndReason.MicrophoneStopped);

        _isTrackingUtterance = false;
        _isRecording = false;
    }

    private void Update()
    {
        if (!IsOwner || !_isRecording || _micClip == null) return;

        int currentPos = Microphone.GetPosition(_selectedDevice);
        if (currentPos < 0) return;

        int stepSamples = 1600; // 100ms analysis step
        int samplesAvailable = (currentPos >= _lastSamplePos) 
            ? (currentPos - _lastSamplePos) 
            : (_micClip.samples - _lastSamplePos + currentPos);

        if (samplesAvailable >= stepSamples)
        {
            float[] buffer = new float[samplesAvailable];
            _micClip.GetData(buffer, _lastSamplePos);
            _lastSamplePos = currentPos;

            AnalyzeAudioSlice(buffer);
        }
    }

    /// <summary>
    /// Phase1: phân thích audio
    /// </summary>
    private void AnalyzeAudioSlice(float[] slice)
    {
        float rms = SpeechDSPUtils.CalculateRMS(slice);
        float zcr = SpeechDSPUtils.CalculateZCR(slice);
        float sliceDuration = (float)slice.Length / _sampleRate;

        bool isVoiced = (rms >= _volumeThreshold) && (zcr <= _maxZcrThreshold);
        
        if (isVoiced)
        {
            if (!_isTrackingUtterance)
                BeginLocalUtterance();

            _currentUtteranceSamples.AddRange(slice);
            _currentVoiceDuration += sliceDuration;
            _silenceDuration = 0f;
            UpdateRealtimeVowel(slice);

            if (Time.unscaledTime >= _nextHeartbeatTime)
            {
                _nextHeartbeatTime = Time.unscaledTime + _heartbeatInterval;
                VoiceHeartbeatRpc(_utteranceSequence, _stableVowel);
            }
        }
        else
        {
            if (_currentUtteranceSamples.Count > 0)
            {
                _silenceDuration += sliceDuration;

                if (_silenceDuration >= SILENCE_TIMEOUT)
                {
                    FinalizeUtterance();
                }
            }
        }
    }

    private void FinalizeUtterance()
    {
        uint sequence = _utteranceSequence;
        VoiceVowel realtimeVowel = _stableVowel;
        float duration = _currentVoiceDuration;
        float[] rawSamples = _currentUtteranceSamples.ToArray();
        bool shouldRecognize = duration >= _minVowelDuration &&
                               !_isProcessing &&
                               Time.time >= _lastSpellTime + _spellCooldown;

        if (_isTrackingUtterance)
        {
            VoiceEndReason reason = duration >= _minVowelDuration
                ? VoiceEndReason.Silence
                : VoiceEndReason.TooShort;
            EndVoiceTrackingRpc(sequence, realtimeVowel, reason);
        }

        _isTrackingUtterance = false;

        if (shouldRecognize)
        {
            bool isSustained = duration >= _sustainedThreshold;
            _ = ProcessSpeechHybridAsync(rawSamples, duration, isSustained, sequence);
        }

        _currentUtteranceSamples.Clear();
        _currentVoiceDuration = 0f;
        _silenceDuration = 0f;
        _stableVowel = VoiceVowel.None;
        _recentVowelVotes.Clear();
    }

    private void BeginLocalUtterance()
    {
        _utteranceSequence++;
        _isTrackingUtterance = true;
        _stableVowel = VoiceVowel.None;
        _recentVowelVotes.Clear();
        _nextHeartbeatTime = Time.unscaledTime;
        BeginVoiceTrackingRpc(_utteranceSequence, VoiceVowel.None);
    }

    private void UpdateRealtimeVowel(float[] slice)
    {
        float[] emphasized = SpeechDSPUtils.ApplyPreEmphasis(slice, 0.97f);
        VoiceVowel vote = VoiceVowel.None;

        if (SpeechDSPUtils.EstimateFormants(emphasized, _sampleRate, out float f1, out float f2))
            vote = ToVoiceVowel(SpeechDSPUtils.ClassifyFormantVowel(f1, f2));

        _recentVowelVotes.Enqueue(vote);
        while (_recentVowelVotes.Count > _vowelWindowSize)
            _recentVowelVotes.Dequeue();

        int u = 0;
        int i = 0;
        int a = 0;
        foreach (VoiceVowel value in _recentVowelVotes)
        {
            if (value == VoiceVowel.U) u++;
            else if (value == VoiceVowel.I) i++;
            else if (value == VoiceVowel.A) a++;
        }

        if (_recentVowelVotes.Count < _minimumStableVotes)
            return;

        int requiredVotes = _minimumStableVotes;
        if (u >= requiredVotes && u >= i && u >= a) _stableVowel = VoiceVowel.U;
        else if (i >= requiredVotes && i >= u && i >= a) _stableVowel = VoiceVowel.I;
        else if (a >= requiredVotes && a >= u && a >= i) _stableVowel = VoiceVowel.A;
    }

    /// <summary>
    /// Giai đoạn 2 & 3: Xử lý Kép (DSP Formants + Sherpa-ONNX) và Đối chiếu Đồng Thuận
    /// </summary>
    private async Task ProcessSpeechHybridAsync(
        float[] rawSamples,
        float duration,
        bool isSustained,
        uint sequence)
    {
        if (_recognizer == null)
        {
            InitRecognizer();
            if (_recognizer == null) return;
        }

        _isProcessing = true;
        try
        {
            string mode = isSustained ? "<color=magenta>[NGÂN DÀI]</color>" : "<color=cyan>[ÂM NGẮN]</color>";

            // 1. Áp dụng Pre-emphasis bù phổ cao tần
            float[] preEmphasized = SpeechDSPUtils.ApplyPreEmphasis(rawSamples, 0.97f);

            // 2. Nhánh A: Trích xuất vân tay Formant vật lý (F1, F2)
            bool hasFormants = SpeechDSPUtils.EstimateFormants(preEmphasized, _sampleRate, out float f1, out float f2);
            SpeechDSPUtils.VowelClass formantVowel = hasFormants 
                ? SpeechDSPUtils.ClassifyFormantVowel(f1, f2) 
                : SpeechDSPUtils.VowelClass.None;

            Debug.Log($"<color=white>[Pipeline GĐ1-2] {mode} Âm {duration:F2}s | Formants: F1={f1:F0}Hz, F2={f2:F0}Hz ──► Kết quả Vật Lý: {formantVowel}</color>");

            // 3. Nhánh B: Giải mã mạng nơ-ron Sherpa-ONNX Zipformer
            string recognizedText = await Task.Run(() =>
            {
                using var stream = _recognizer.CreateStream();
                if (stream == null || stream.Handle == IntPtr.Zero)
                    return string.Empty;

                stream.AcceptWaveform(_sampleRate, preEmphasized);
                _recognizer.Decode(stream);

                var res = stream.Result;
                return res != null ? res.Text : string.Empty;
            });

            string rawText = recognizedText.Trim();
            Debug.Log($"<color=yellow>[Sherpa-ONNX ASR Token]</color> \"{rawText}\"");

            // 4. Giai đoạn 3: Đối chiếu Đồng Thuận (Consensus Decision Gate)
            string detectedSpell = EvaluateConsensus(formantVowel, rawText, isSustained);

            if (!string.IsNullOrEmpty(detectedSpell))
            {
                if (!IsSpawned || !IsOwner)
                    return;

                ConfirmVoiceRecognitionRpc(sequence, ParseSpellVowel(detectedSpell));
                _lastSpellTime = Time.time;
                Debug.Log($"<color=green>[Consensus Gate] ★★★ DUYỆT THÀNH CÔNG '{detectedSpell}' (Ngân dài: {isSustained}) ★★★</color>");
                CastSpellRpc(sequence, detectedSpell);
            }
            else
            {
                Debug.Log($"<color=grey>[Consensus Gate] ✘ Từ chối ngoại âm (Formant={formantVowel}, ASR=\"{rawText}\")</color>");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SherpaVoiceController] Processing error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// Giai đoạn 3: Quyết định đồng thuận giữa Vật lý (Formant) và AI (ASR)
    /// </summary>
    private string EvaluateConsensus(SpeechDSPUtils.VowelClass formantVowel, string rawText, bool isSustained)
    {
        string asrVowel = ParseAsrVowel(rawText);

        string finalVowel = null;

        // Trường hợp 1: Cả hai bên cùng đồng thuận 100%
        if (formantVowel == SpeechDSPUtils.VowelClass.U && asrVowel == "U") finalVowel = "U";
        else if (formantVowel == SpeechDSPUtils.VowelClass.I && asrVowel == "I") finalVowel = "I";
        else if (formantVowel == SpeechDSPUtils.VowelClass.A && asrVowel == "A") finalVowel = "A";

        // Trường hợp 2: Formant vật lý xác nhận chắc chắn (F1/F2 chuẩn) và ASR có từ khóa tương thích
        else if (formantVowel == SpeechDSPUtils.VowelClass.U && (asrVowel == "U" || string.IsNullOrEmpty(asrVowel)))
            finalVowel = "U";
        else if (formantVowel == SpeechDSPUtils.VowelClass.I && (asrVowel == "I" || string.IsNullOrEmpty(asrVowel)))
            finalVowel = "I";
        else if (formantVowel == SpeechDSPUtils.VowelClass.A && (asrVowel == "A" || string.IsNullOrEmpty(asrVowel)))
            finalVowel = "A";

        // Trường hợp 3: ASR nhận diện rõ ràng nguyên âm đơn
        else if (!string.IsNullOrEmpty(asrVowel))
            finalVowel = asrVowel;

        if (finalVowel == null) return null;

        // Phân loại Âm ngắn vs Ngân dài (Charged Spell)
        return isSustained ? $"{finalVowel}_LONG" : finalVowel;
    }

    /// <summary>
    /// Phân tích ký tự ASR, cho phép các biến thể âm học tự nhiên tiếng Việt
    /// </summary>
    private static string ParseAsrVowel(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        string cleaned = Regex.Replace(rawText.ToUpperInvariant(), @"[^\w\s\u00C0-\u1EF9]", " ").Trim();
        string[] words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var w in words)
        {
            // Nhóm U: U, Ú, Ù, Ư... và các biến thể âm tròn môi khi ngân dài: Ô, Ồ, Ố, O, Ò, Ó, HU, HÚ, THÌ
            if (Regex.IsMatch(w, @"^(H|TH)?[UÚÙỦŨỤƯỨỪỬỮỰÔỐỒỔỖỘOÓÒỎÕỌW]+$") || w == "THÌ")
                return "U";

            // Nhóm I: I, Í, Ì... Y, Ý, Ỳ... E, É, È, Ê... HI, HÍ
            if (Regex.IsMatch(w, @"^[H]?[IÍÌỈĨỊYÝỲỶỸỴEÉÈẺẼẸÊẾỀỂỄỆ]+$"))
                return "I";

            // Nhóm A: A, Á, À... Ă, Â... HA, HÁ, AH
            if (Regex.IsMatch(w, @"^[H]?[AÁÀẢÃẠĂẮẰẲẴẶÂẤẦẨẪẬ]+[H]?$"))
                return "A";
        }

        return null;
    }

    private static VoiceVowel ToVoiceVowel(SpeechDSPUtils.VowelClass vowel)
    {
        return vowel switch
        {
            SpeechDSPUtils.VowelClass.U => VoiceVowel.U,
            SpeechDSPUtils.VowelClass.I => VoiceVowel.I,
            SpeechDSPUtils.VowelClass.A => VoiceVowel.A,
            _ => VoiceVowel.None
        };
    }

    private static VoiceVowel ParseSpellVowel(string spellType)
    {
        if (string.IsNullOrEmpty(spellType)) return VoiceVowel.None;
        if (spellType.StartsWith("U", StringComparison.Ordinal)) return VoiceVowel.U;
        if (spellType.StartsWith("I", StringComparison.Ordinal)) return VoiceVowel.I;
        if (spellType.StartsWith("A", StringComparison.Ordinal)) return VoiceVowel.A;
        return VoiceVowel.None;
    }

    [Rpc(SendTo.Server)]
    private void BeginVoiceTrackingRpc(
        uint sequence,
        VoiceVowel initialVowel,
        RpcParams rpcParams = default)
    {
        VoiceRoundManager.Instance?.BeginVoice(
            this,
            rpcParams.Receive.SenderClientId,
            sequence,
            initialVowel);
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void VoiceHeartbeatRpc(
        uint sequence,
        VoiceVowel vowel,
        RpcParams rpcParams = default)
    {
        VoiceRoundManager.Instance?.Heartbeat(
            this,
            rpcParams.Receive.SenderClientId,
            sequence,
            vowel);
    }

    [Rpc(SendTo.Server)]
    private void EndVoiceTrackingRpc(
        uint sequence,
        VoiceVowel vowel,
        VoiceEndReason reason,
        RpcParams rpcParams = default)
    {
        VoiceRoundManager.Instance?.EndVoice(
            this,
            rpcParams.Receive.SenderClientId,
            sequence,
            vowel,
            reason);
    }

    [Rpc(SendTo.Server)]
    private void ConfirmVoiceRecognitionRpc(
        uint sequence,
        VoiceVowel vowel,
        RpcParams rpcParams = default)
    {
        VoiceRoundManager.Instance?.ConfirmRecognition(
            this,
            rpcParams.Receive.SenderClientId,
            sequence,
            vowel);
    }

    internal void ApplyServerTracking(
        VoiceTrackState state,
        VoiceVowel vowel,
        float continuousDuration,
        VoiceEndReason endReason,
        bool isActive)
    {
        if (!IsServer)
            return;

        TrackedState.Value = state;
        TrackedVowel.Value = vowel;
        TrackedContinuousDuration.Value = continuousDuration;
        TrackedEndReason.Value = endReason;
        IsActivelyVoicing.Value = isActive;
    }

    [Rpc(SendTo.Server)]
    private void CastSpellRpc(uint sequence, string spellType)
    {
        if (string.IsNullOrEmpty(spellType)) return;

        VoiceVowel vowel = ParseSpellVowel(spellType);
        if (VoiceRoundManager.Instance == null ||
            !VoiceRoundManager.Instance.TryGetSnapshot(OwnerClientId, out VoicePlayerSnapshot snapshot) ||
            snapshot.Sequence != sequence ||
            snapshot.State != VoiceTrackState.Completed ||
            snapshot.Vowel != vowel)
        {
            Debug.LogWarning(
                $"[Server] Rejected voice spell '{spellType}' from player {OwnerClientId}: " +
                "the authoritative voice track is not complete.");
            return;
        }

        Debug.Log($"[Server] Player {OwnerClientId} cast Sherpa spell '{spellType}'");
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
        Debug.Log($"<color=yellow>[Sherpa-ONNX Magic] Player {senderClientId} triggered '{spellType}'!</color>");

        if (senderClientId == OwnerClientId)
        {
            switch (spellType)
            {
                case "U":
                    Debug.Log("<color=cyan>[Action U]</color>");
                    break;
                case "U_LONG":
                    Debug.Log("<color=cyan>[Action U_LONG]</color>");
                    break;

                case "I":
                    Debug.Log("<color=magenta>[Action I]</color>");
                    break;
                case "I_LONG":
                    Debug.Log("<color=magenta>[Action I_LONG]</color>");
                    break;

                case "A":
                    Debug.Log("<color=red>[Action A]</color>");
                    break;
                case "A_LONG":
                    Debug.Log("<color=red>[Action A_LONG]</color>");
                    break;
            }
        }
    }
}
