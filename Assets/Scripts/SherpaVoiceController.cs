using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Eitan.SherpaONNXUnity.Runtime.Native;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Advanced Voice Controller using Sherpa-ONNX Zipformer Vietnamese model (30M-int8).
/// Features 3-Layer Speech Engineering:
/// 1. DSP Audio Filtering: Zero Crossing Rate (ZCR) to drop breath/noise and measure sustained vowel duration (Ngân dài).
/// 2. Fast Greedy Search Decoding on CPU.
/// 3. Strict Out-Of-Vocabulary (OOV) Rejection: Rejects any words/ambient sounds not matching target vowels.
/// Supports both Short Spell ("U", "I", "A") and Charged Spell ("U_LONG", "I_LONG", "A_LONG").
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

    [Header("Vowel & Noise Filtering (DSP)")]
    [SerializeField] private float _volumeThreshold = 0.0035f;
    [SerializeField] private float _maxZcrThreshold = 0.28f;       // Filters out breath/fricatives/clicks (ZCR > 0.28)
    [SerializeField] private float _minVowelDuration = 0.18f;      // Discard clicks/pops shorter than 0.18s
    [SerializeField] private float _sustainedThreshold = 0.70f;    // >= 0.7s continuous vowel is "Ngân dài"
    [SerializeField] private float _spellCooldown = 0.6f;
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
            config.HotwordsFile = ""; // greedy_search requires empty hotwords string

            _recognizer = new OfflineRecognizer(config);
            Debug.Log("<color=green>[SherpaVoiceController] Khởi tạo thành công Sherpa-ONNX Zipformer VN (3-Layer Vowel Filter)!</color>");
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

        Debug.Log("<color=green>[SherpaVoiceController] Microphone active! Đang lắng nghe nguyên âm U, I, A (hỗ trợ cả âm ngắn & ngân dài)...</color>");
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

        // Process audio in 100ms slices (1600 samples)
        int stepSamples = 1600;
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
    /// Layer 1: DSP Audio Energy & Zero Crossing Rate (ZCR) filtering.
    /// Tracks sustained vowel duration ("Ngân dài").
    /// </summary>
    private void AnalyzeAudioSlice(float[] slice)
    {
        float rms = CalculateRMS(slice);
        float zcr = CalculateZCR(slice);

        float sliceDuration = (float)slice.Length / _sampleRate;

        // Condition for valid human voiced sound:
        // RMS above volume threshold AND ZCR < 0.28 (Vowels have periodic low-ZCR, noise/breath has high erratic ZCR)
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

                // When silence exceeds timeout, the vowel utterance has finished
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
            float[] samplesToRecognize = _currentUtteranceSamples.ToArray();
            float duration = _currentVoiceDuration;
            bool isSustained = duration >= _sustainedThreshold;

            _ = ProcessSpeechAsync(samplesToRecognize, duration, isSustained);
        }

        // Reset buffer
        _currentUtteranceSamples.Clear();
        _currentVoiceDuration = 0f;
        _silenceDuration = 0f;
    }

    private async Task ProcessSpeechAsync(float[] samples, float duration, bool isSustained)
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
            Debug.Log($"<color=white>[Sherpa-ONNX VN] {mode} Âm thanh kết thúc ({duration:F2}s), đang giải mã...</color>");

            string recognizedText = await Task.Run(() =>
            {
                using var stream = _recognizer.CreateStream();
                if (stream == null || stream.Handle == IntPtr.Zero)
                {
                    return string.Empty;
                }

                stream.AcceptWaveform(_sampleRate, samples);
                _recognizer.Decode(stream);

                var res = stream.Result;
                return res != null ? res.Text : string.Empty;
            });

            if (!string.IsNullOrWhiteSpace(recognizedText))
            {
                string raw = recognizedText.Trim();
                Debug.Log($"<color=yellow>[Sherpa-ONNX Heard]</color> \"{raw}\"");

                // Layer 3: Strict Regex & Out-of-Vocabulary (OOV) Rejection
                string detectedSpell = ParseAndFilterVowels(raw, isSustained);
                if (!string.IsNullOrEmpty(detectedSpell))
                {
                    _lastSpellTime = Time.time;
                    Debug.Log($"<color=green>[Sherpa-ONNX Spell] MATCHED '{detectedSpell}' (Sustained: {isSustained})!</color>");
                    CastSpellRpc(detectedSpell);
                }
                else
                {
                    Debug.Log($"<color=grey>[Sherpa-ONNX Filter] Đã loại bỏ ngoại âm: \"{raw}\"</color>");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SherpaVoiceController] Recognition error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
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

    private float CalculateZCR(float[] samples)
    {
        if (samples == null || samples.Length < 2) return 0f;
        int count = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if ((samples[i] >= 0f && samples[i - 1] < 0f) || (samples[i] < 0f && samples[i - 1] >= 0f))
                count++;
        }
        return (float)count / samples.Length;
    }

    /// <summary>
    /// Layer 3: Strict Vowel Filtering & Out-of-Vocabulary (OOV) Rejection.
    /// Rejects any word containing non-vowel consonants or noise.
    /// Differentiates between short vowels and sustained/elongated vowels ("Ngân dài").
    /// </summary>
    public static string ParseAndFilterVowels(string rawText, bool isSustainedByDuration)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        // Chuẩn hóa văn bản thành chữ in hoa và bỏ dấu câu
        string cleaned = Regex.Replace(rawText.ToUpperInvariant(), @"[^\w\s\u00C0-\u1EF9]", " ").Trim();
        string[] words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        bool hasU = false;
        bool hasI = false;
        bool hasA = false;
        bool hasRepeatedLetters = false;

        foreach (var w in words)
        {
            if (w.Length >= 2) hasRepeatedLetters = true;

            // Nhóm U:
            // 1. Bao gồm các nguyên âm gốc: U, Ú, Ù, Ủ, Ũ, Ụ, Ư, Ứ, Ừ, Ử, Ữ, Ự
            // 2. Các nguyên âm tròn môi đồng dạng khi ngân dài: Ô, Ố, Ồ, Ổ, Ỗ, Ộ, O, Ó, Ò, Ỏ, Õ, Ọ, W
            // 3. Các tiền âm bật hơi nhẹ: H (HU, HÚ, HÙ, HÔ), TH (THU, THÌ, THỒ)
            if (Regex.IsMatch(w, @"^(H|TH)?[UÚÙỦŨỤƯỨỪỬỮỰÔỐỒỔỖỘOÓÒỎÕỌW]+$") || w == "THÌ")
            {
                hasU = true;
                continue;
            }

            // Nhóm I:
            // Bao gồm: I, Í, Ì, Ỉ, Ĩ, Ị, Y, Ý, Ỳ, Ỷ, Ỹ, Ỵ, E, É, È, Ẻ, Ẽ, Ẹ, Ê, Ế, Ề, Ể, Ễ, Ệ
            // Và tiền âm thở nhẹ: HI, HÍ, HÌ...
            if (Regex.IsMatch(w, @"^[H]?[IÍÌỈĨỊYÝỲỶỸỴEÉÈẺẼẸÊẾỀỂỄỆ]+$"))
            {
                hasI = true;
                continue;
            }

            // Nhóm A:
            // Bao gồm: A, Á, À, Ả, Ã, Ạ, Ă, Ắ, Ằ, Ẳ, Ẵ, Ặ, Â, Ấ, Ầ, Ẩ, Ẫ, Ậ
            // Và tiền âm/hậu âm thở nhẹ: HA, HÁ, HÀ, AH...
            if (Regex.IsMatch(w, @"^[H]?[AÁÀẢÃẠĂẮẰẲẴẶÂẤẦẨẪẬ]+[H]?$"))
            {
                hasA = true;
                continue;
            }
        }

        bool isLong = isSustainedByDuration || hasRepeatedLetters;

        if (hasU) return isLong ? "U_LONG" : "U";
        if (hasI) return isLong ? "I_LONG" : "I";
        if (hasA) return isLong ? "A_LONG" : "A";

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
                    Debug.Log("<color=cyan>[Action U] Lướt nhanh tiêu chuẩn!</color>");
                    break;
                case "U_LONG":
                    Debug.Log("<color=cyan>[Action U_LONG] ★★★ SIÊU LƯỚT THẦN TỐC (MEGA CHARGED DASH) ★★★</color>");
                    break;

                // PHÉP I: KHIÊN
                case "I":
                    Debug.Log("<color=magenta>[Action I] Khiên bảo vệ tiêu chuẩn!</color>");
                    break;
                case "I_LONG":
                    Debug.Log("<color=magenta>[Action I_LONG] ★★★ KHIÊN BẤT HOẠI TOÀN NĂNG (IMMORTAL BARRIER) ★★★</color>");
                    break;

                // PHÉP A: NHẢY / XUNG KÍCH
                case "A":
                    Debug.Log("<color=red>[Action A] Nhảy cao tiêu chuẩn!</color>");
                    break;
                case "A_LONG":
                    Debug.Log("<color=red>[Action A_LONG] ★★★ ĐẠI ĐỊA CHẤN XUNG KÍCH (EARTHQUAKE SHOCKWAVE) ★★★</color>");
                    break;
            }
        }
    }
}
