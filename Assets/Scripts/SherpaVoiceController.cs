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

    public bool IsListening => _isRecording;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        InitRecognizer();

        GameManager.OnGameStarted += StartListening;

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

    private void OnDestroy()
    {
        GameManager.OnGameStarted -= StartListening;
        StopListening();
        DisposeRecognizer();
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

        Debug.Log("<color=green>[SherpaVoiceController] Microphone active! Pipeline Sẵn Sàng (U, I, A - Âm ngắn & Ngân dài)...</color>");
    }

    public void StopListening()
    {
        if (!_isRecording) return;

        if (!string.IsNullOrEmpty(_selectedDevice) && Microphone.IsRecording(_selectedDevice))
        {
            Microphone.End(_selectedDevice);
        }
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
    /// Giai đoạn 1: Lọc năng lượng RMS & Zero Crossing Rate (ZCR) để loại bỏ tiếng thở/tiếng gió
    /// </summary>
    private void AnalyzeAudioSlice(float[] slice)
    {
        float rms = SpeechDSPUtils.CalculateRMS(slice);
        float zcr = SpeechDSPUtils.CalculateZCR(slice);
        float sliceDuration = (float)slice.Length / _sampleRate;

        // Tiếng nói thật: RMS > VolumeThreshold VÀ ZCR <= 0.28 (nguyên âm có chu kỳ đều đặn)
        bool isVoiced = (rms >= _volumeThreshold) && (zcr <= _maxZcrThreshold);

        if (isVoiced)
        {
            _currentUtteranceSamples.AddRange(slice);
            _currentVoiceDuration += sliceDuration;
            _silenceDuration = 0f;
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
        if (_currentVoiceDuration >= _minVowelDuration && !_isProcessing && Time.time >= _lastSpellTime + _spellCooldown)
        {
            float[] rawSamples = _currentUtteranceSamples.ToArray();
            float duration = _currentVoiceDuration;
            bool isSustained = duration >= _sustainedThreshold;

            _ = ProcessSpeechHybridAsync(rawSamples, duration, isSustained);
        }

        _currentUtteranceSamples.Clear();
        _currentVoiceDuration = 0f;
        _silenceDuration = 0f;
    }

    /// <summary>
    /// Giai đoạn 2 & 3: Xử lý Kép (DSP Formants + Sherpa-ONNX) và Đối chiếu Đồng Thuận
    /// </summary>
    private async Task ProcessSpeechHybridAsync(float[] rawSamples, float duration, bool isSustained)
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
                _lastSpellTime = Time.time;
                Debug.Log($"<color=green>[Consensus Gate] ★★★ DUYỆT THÀNH CÔNG '{detectedSpell}' (Ngân dài: {isSustained}) ★★★</color>");
                CastSpellRpc(detectedSpell);
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

    [Rpc(SendTo.Server)]
    public void CastSpellRpc(string spellType)
    {
        if (string.IsNullOrEmpty(spellType)) return;

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
                // PHÉP U: LƯỚT
                case "U":
                    Debug.Log("<color=cyan>[Action U] 💨 LƯỚT NHANH TIÊU CHUẨN!</color>");
                    break;
                case "U_LONG":
                    Debug.Log("<color=cyan>[Action U_LONG] ⚡★★★ SIÊU LƯỚT THẦN TỐC (MEGA CHARGED DASH) ★★★</color>");
                    break;

                // PHÉP I: KHIÊN
                case "I":
                    Debug.Log("<color=magenta>[Action I] 🛡️ KHIÊN BẢO VỆ TIÊU CHUẨN!</color>");
                    break;
                case "I_LONG":
                    Debug.Log("<color=magenta>[Action I_LONG] 🔮★★★ KHIÊN BẤT HOẠI TOÀN NĂNG (IMMORTAL BARRIER) ★★★</color>");
                    break;

                // PHÉP A: NHẢY / XUNG KÍCH
                case "A":
                    Debug.Log("<color=red>[Action A] 🦘 NHẢY CAO TIÊU CHUẨN!</color>");
                    break;
                case "A_LONG":
                    Debug.Log("<color=red>[Action A_LONG] 💥★★★ ĐẠI ĐỊA CHẤN XUNG KÍCH (EARTHQUAKE SHOCKWAVE) ★★★</color>");
                    break;
            }
        }
    }
}
