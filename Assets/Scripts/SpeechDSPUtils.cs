using System;
using UnityEngine;

/// <summary>
/// Digital Signal Processing (DSP) utilities for Speech AI:
/// - Pre-emphasis filtering (high-frequency spectral tilt compensation)
/// - Zero Crossing Rate (ZCR) & RMS energy calculation
/// - Formant Tracking (F1 & F2) using Linear Predictive Coding (LPC) and Levinson-Durbin recursion
/// - Acoustic vowel classification (/u/, /i/, /a/)
/// </summary>
public static class SpeechDSPUtils
{
    public enum VowelClass
    {
        None,
        U,
        I,
        A
    }

    /// <summary>
    /// Applies high-frequency pre-emphasis: y[n] = x[n] - alpha * x[n-1]
    /// </summary>
    public static float[] ApplyPreEmphasis(float[] signal, float alpha = 0.97f)
    {
        if (signal == null || signal.Length == 0) return Array.Empty<float>();

        float[] output = new float[signal.Length];
        output[0] = signal[0];
        for (int i = 1; i < signal.Length; i++)
        {
            output[i] = signal[i] - alpha * signal[i - 1];
        }
        return output;
    }

    /// <summary>
    /// Calculates Root Mean Square (RMS) volume of the audio signal.
    /// </summary>
    public static float CalculateRMS(float[] samples)
    {
        if (samples == null || samples.Length == 0) return 0f;

        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
            sum += samples[i] * samples[i];

        return Mathf.Sqrt(sum / samples.Length);
    }

    /// <summary>
    /// Calculates Zero Crossing Rate (ZCR). Voiced speech has low ZCR (<0.25),
    /// while unvoiced noise/breath has high erratic ZCR (>0.30).
    /// </summary>
    public static float CalculateZCR(float[] samples)
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
    /// Estimates the first two formants (F1, F2) using Autocorrelation LPC and Spectral Envelope Peak-Picking.
    /// </summary>
    public static bool EstimateFormants(float[] samples, int sampleRate, out float f1, out float f2)
    {
        f1 = 0f;
        f2 = 0f;

        if (samples == null || samples.Length < 512)
            return false;

        // Take a stable 512-sample window from the middle of the utterance
        int windowSize = 512;
        int startIndex = (samples.Length - windowSize) / 2;
        float[] windowed = new float[windowSize];

        // Apply Hamming window
        for (int i = 0; i < windowSize; i++)
        {
            float hamming = 0.54f - 0.46f * Mathf.Cos(2f * Mathf.PI * i / (windowSize - 1));
            windowed[i] = samples[startIndex + i] * hamming;
        }

        // LPC Order: p = 14
        int p = 14;
        float[] r = new float[p + 1];

        // 1. Autocorrelation
        for (int k = 0; k <= p; k++)
        {
            float sum = 0f;
            for (int n = 0; n < windowSize - k; n++)
                sum += windowed[n] * windowed[n + k];
            r[k] = sum;
        }

        if (r[0] <= 1e-7f) return false;

        // 2. Levinson-Durbin Recursion
        float[] a = new float[p + 1];
        a[0] = 1f;
        float[] aPrev = new float[p + 1];
        float error = r[0];

        for (int i = 1; i <= p; i++)
        {
            float lambda = 0f;
            for (int j = 0; j < i; j++)
                lambda += a[j] * r[i - j];

            float kRefl = -lambda / error;

            Array.Copy(a, aPrev, i);
            a[i] = kRefl;
            for (int j = 1; j < i; j++)
                a[j] = aPrev[j] + kRefl * aPrev[i - j];

            error *= (1f - kRefl * kRefl);
            if (error <= 1e-7f) break;
        }

        // 3. Scan LPC Spectrum envelope from 150 Hz to 3000 Hz to locate resonance peaks (Formants)
        int numBins = 128;
        float minFreq = 150f;
        float maxFreq = 3000f;
        float stepFreq = (maxFreq - minFreq) / numBins;

        float[] spectrum = new float[numBins];
        for (int b = 0; b < numBins; b++)
        {
            float freq = minFreq + b * stepFreq;
            float omega = 2f * Mathf.PI * freq / sampleRate;

            float re = 1f;
            float im = 0f;
            for (int k = 1; k <= p; k++)
            {
                re += a[k] * Mathf.Cos(k * omega);
                im -= a[k] * Mathf.Sin(k * omega);
            }

            float power = 1f / (re * re + im * im + 1e-6f);
            spectrum[b] = power;
        }

        // 4. Find local peaks
        int peak1Index = -1;
        int peak2Index = -1;
        float maxPeak1 = 0f;
        float maxPeak2 = 0f;

        for (int i = 1; i < numBins - 1; i++)
        {
            if (spectrum[i] > spectrum[i - 1] && spectrum[i] > spectrum[i + 1])
            {
                float freq = minFreq + i * stepFreq;

                // F1 candidate (200 Hz - 1000 Hz)
                if (freq <= 1000f && spectrum[i] > maxPeak1)
                {
                    maxPeak1 = spectrum[i];
                    peak1Index = i;
                }
                // F2 candidate (800 Hz - 2800 Hz)
                else if (freq > 800f && spectrum[i] > maxPeak2)
                {
                    maxPeak2 = spectrum[i];
                    peak2Index = i;
                }
            }
        }

        if (peak1Index != -1)
            f1 = minFreq + peak1Index * stepFreq;

        if (peak2Index != -1)
            f2 = minFreq + peak2Index * stepFreq;
        else if (f1 > 0f)
            f2 = f1 * 2.5f; // Reasonable acoustic approximation if F2 is blurred

        return f1 > 0f;
    }

    /// <summary>
    /// Classifies vowel identity purely from physical acoustic formant frequencies (F1, F2).
    /// </summary>
    public static VowelClass ClassifyFormantVowel(float f1, float f2)
    {
        // Nhóm U: Khoang miệng sau, khẩu hình tròn (Low F1, Low F2)
        // F1: 250 - 550 Hz | F2: 600 - 1300 Hz
        if (f1 <= 550f && f2 <= 1350f)
            return VowelClass.U;

        // Nhóm I: Khoang miệng trước dẹt (Low F1, High F2)
        // F1: 200 - 480 Hz | F2: 1700 - 2900 Hz
        if (f1 <= 480f && f2 >= 1650f)
            return VowelClass.I;

        // Nhóm A: Vòm họng mở to (High F1, Mid F2)
        // F1: 520 - 1000 Hz | F2: 950 - 1800 Hz
        if (f1 >= 520f && f2 >= 900f && f2 <= 1850f)
            return VowelClass.A;

        return VowelClass.None;
    }
}
