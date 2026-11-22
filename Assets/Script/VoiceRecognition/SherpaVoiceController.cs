using System;
using Unity.Netcode;
using UnityEngine;

public enum VoiceVowel : byte
{
    None,
    U,
    I,
    A
}

/// <summary>
/// Module cảm biến và nhận diện ngân âm dài (Sustained Vowel Sensor) chạy trên từng Client:
/// - Nhận lệnh bật Mic từ VoiceManager (Server) thông qua StartListeningRpc.
/// - Giám sát âm thanh theo thời gian thực bằng DSP (RMS, ZCR, Formants F1/F2).
/// - Nếu người chơi ngân dài ổn định: OK (tiếp tục ngân, không có gì xảy ra).
/// - Nếu đứt hơi hoặc đổi âm: Báo cáo thẳng lên VoiceManager trên Server qua ReportDefeatServerRpc.
/// </summary>
public class SherpaVoiceController : NetworkBehaviour
{
    [Header("Audio & Noise Gating (DSP)")]
    [SerializeField] private float _volumeThreshold = 0.0035f;
    [SerializeField] private float _maxZcrThreshold = 0.28f;       // Lọc tiếng thở / tiếng gió xì (ZCR > 0.28)
    [SerializeField] private float _silenceTolerance = 0.18f;      // Ngừng tiếng quá 0.18s = Đứt hơi
    [SerializeField] private float _minChantStartDuration = 0.20f; // Cần giữ ổn định ít nhất 0.2s để tính là bắt đầu ngân
    [SerializeField] private int   _sampleRate = 16000;

    private AudioClip _micClip;
    private string    _selectedDevice;
    private int       _lastSamplePos;
    private bool      _isRecording;

    // Trạng thái theo dõi ngân giọng
    private bool       _isChanting;
    private VoiceVowel _currentChantingVowel = VoiceVowel.None;
    private float      _chantingDuration;
    private float      _silenceTimer;
    private float      _preChantTimer;
    private VoiceVowel _candidateVowel = VoiceVowel.None;

    public bool IsListening => _isRecording;

    // ── Unity & NGO Lifecycle ──────────────────────────────────────────────────
    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        StopListening();
    }

    public override void OnDestroy()
    {
        StopListening();
        base.OnDestroy();
    }

    /// <summary>
    /// Được gọi thẳng từ VoiceManager (trên Server) xuống máy Client để kích hoạt Micro.
    /// </summary>
    [Rpc(SendTo.Owner)]
    public void StartListeningRpc()
    {
        StartListening();
    }

    // ── Quản lý Microphone ─────────────────────────────────────────────────────
    public void StartListening()
    {
        if (_isRecording) return;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[SherpaVoiceController] Không tìm thấy thiết bị Microphone!");
            return;
        }

        _selectedDevice = Microphone.devices[0];
        _micClip = Microphone.Start(_selectedDevice, true, 20, _sampleRate);
        _lastSamplePos = 0;
        _isRecording = true;

        ResetChantState();

        Debug.Log("<color=green>[SherpaVoiceController] Micro đã kích hoạt! Bắt đầu giám sát ngân âm (U, I, A)...</color>");
    }

    public void StopListening()
    {
        if (!_isRecording) return;

        if (!string.IsNullOrEmpty(_selectedDevice) && Microphone.IsRecording(_selectedDevice))
        {
            Microphone.End(_selectedDevice);
        }

        _isRecording = false;
        ResetChantState();
    }

    // ── Xử lý âm thanh thời gian thực (Mỗi 100ms) ──────────────────────────────
    private void Update()
    {
        if (!IsOwner || !_isRecording || _micClip == null) return;

        int currentPos = Microphone.GetPosition(_selectedDevice);
        if (currentPos < 0 || currentPos == _lastSamplePos) return;

        int stepSamples = _sampleRate / 10; 
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

    private void AnalyzeAudioSlice(float[] slice)
    {
        float rms = SpeechDSPUtils.CalculateRMS(slice);
        float zcr = SpeechDSPUtils.CalculateZCR(slice);
        float sliceDuration = (float)slice.Length / _sampleRate;

        // Điều kiện có tiếng người phát âm nguyên âm: âm lượng đủ to và sóng âm mượt (ít đổi dấu)
        bool isVoiced = (rms >= _volumeThreshold) && (zcr <= _maxZcrThreshold);

        if (isVoiced)
        {
            _silenceTimer = 0f;

            // Đo Formant F1, F2 để tìm nguyên âm tức thời
            float[] emphasized = SpeechDSPUtils.ApplyPreEmphasis(slice, 0.97f);
            VoiceVowel sliceVowel = VoiceVowel.None;

            if (SpeechDSPUtils.EstimateFormants(emphasized, _sampleRate, out float f1, out float f2))
            {
                sliceVowel = ToVoiceVowel(SpeechDSPUtils.ClassifyFormantVowel(f1, f2));
            }

            // Giai đoạn 1: Chưa vào trạng thái ngân -> Cần giữ ổn định tối thiểu 0.2s để xác nhận âm
            if (!_isChanting)
            {
                if (sliceVowel != VoiceVowel.None)
                {
                    if (sliceVowel == _candidateVowel)
                    {
                        _preChantTimer += sliceDuration;
                        if (_preChantTimer >= _minChantStartDuration)
                        {
                            // Xác nhận bắt đầu ngân âm!
                            _isChanting = true;
                            _currentChantingVowel = sliceVowel;
                            _chantingDuration = _preChantTimer;
                            Debug.Log($"<color=cyan>[Voice] Người chơi {OwnerClientId} bắt đầu ngân âm '{_currentChantingVowel}'...</color>");
                        }
                    }
                    else
                    {
                        _candidateVowel = sliceVowel;
                        _preChantTimer = sliceDuration;
                    }
                }
            }
            // Giai đoạn 2: Đang trong quá trình ngân giọng
            else
            {
                _chantingDuration += sliceDuration;

                // KIỂM TRA ĐỔI ÂM: Nếu phát hiện chuyển hẳn sang nguyên âm khác
                if (sliceVowel != VoiceVowel.None && sliceVowel != _currentChantingVowel)
                {
                    string reason = $"Đổi âm (chuyển từ '{_currentChantingVowel}' sang '{sliceVowel}')";
                    ReportInterruption(_currentChantingVowel, _chantingDuration, reason);
                }
                // Nếu ngân đúng âm: TIẾP TỤC NGÂN (Dù là 5s, 10s... KHÔNG CÓ GÌ XẢY RA HẾT)
            }
        }
        else
        {
            _preChantTimer = 0f;
            _candidateVowel = VoiceVowel.None;

            // KIỂM TRA ĐỨT HƠI / NGẮT QUÃNG: Nếu đang ngân mà âm lượng tụt / im lặng quá thời gian cho phép
            if (_isChanting)
            {
                _silenceTimer += sliceDuration;
                if (_silenceTimer >= _silenceTolerance)
                {
                    string reason = "Đứt hơi (ngắt quãng / hụt hơi)";
                    ReportInterruption(_currentChantingVowel, _chantingDuration, reason);
                }
            }
        }
    }

    // ── Báo cáo ngắt quãng & Gửi về VoiceManager trên Server ────────────────
    private void ReportInterruption(VoiceVowel vowel, float duration, string reason)
    {
        // 1. Dừng mic của người chơi này vì đã phạm quy
        StopListening();

        // 2. Báo cáo thẳng lên VoiceManager trên Server để xử lý thua cuộc
        if (VoiceManager.Instance != null)
        {
            VoiceManager.Instance.ReportDefeatServerRpc(OwnerClientId, vowel, duration, reason);
        }
        else
        {
            Debug.LogWarning($"[SherpaVoiceController] VoiceManager chưa sẵn sàng! Không thể báo cáo vi phạm của Player {OwnerClientId}");
        }
    }

    private void ResetChantState()
    {
        _isChanting = false;
        _currentChantingVowel = VoiceVowel.None;
        _candidateVowel = VoiceVowel.None;
        _chantingDuration = 0f;
        _silenceTimer = 0f;
        _preChantTimer = 0f;
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
}
