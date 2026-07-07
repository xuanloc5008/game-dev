using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Handles single-player demo scene start and quit buttons.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class DemoManager : MonoBehaviour
{
    [SerializeField] private PlayerMovement _player;

    private UIDocument _doc;

    private void Awake() => _doc = GetComponent<UIDocument>();

    private void OnEnable()
    {
        var root = _doc.rootVisualElement;
        root.Q<Button>("start-btn")?.RegisterCallback<ClickEvent>(_ => OnStart());
        root.Q<Button>("quit-btn")?.RegisterCallback<ClickEvent>(_ => OnQuit());
    }

    private void OnStart()
    {
        _doc.rootVisualElement.style.display = DisplayStyle.None;
        _player.StartPlaying();
    }

    private void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
