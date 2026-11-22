using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Manages in-game HUD visibility. ESC menu handled by PauseUI on same GameObject.
/// </summary>
public class HudUI : MonoBehaviour
{
    private VisualElement _root;

    private void Start()
    {
        _root = GetComponent<UIDocument>().rootVisualElement;
        _root.style.display = DisplayStyle.None;
        GameManager.OnGameStarted += Show;
    }

    private void OnDestroy() => GameManager.OnGameStarted -= Show;

    private void Show() => _root.style.display = DisplayStyle.Flex;
}
