using System;

public interface IVoiceSessionGate
{
    bool IsVoiceSessionActive { get; }

    event Action OnSessionActivated;
}