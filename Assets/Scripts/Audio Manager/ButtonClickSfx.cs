using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Gives every <see cref="Button"/> in the Jannah Garden scene the same click, so no button is
/// silent and no two sound different.
///
/// Buttons come from the scene, from prefabs spawned at runtime (shop cards, inventory slots) and
/// from panels enabled late, so instead of wiring each one this watches Unity's own list of enabled
/// selectables and hooks any button it has not seen yet. A button is only clickable once it is
/// enabled, and enabling is what puts it on that list, so none is missed. Handlers must therefore
/// not play <see cref="SoundEffect.ButtonClick"/> themselves, or the click doubles up.
/// </summary>
public class ButtonClickSfx : MonoBehaviour
{
    private const string GardenSceneName = "Jannah Garden";

    /// <summary>Buttons already hooked, by instance id. A destroyed button's id is never reused.</summary>
    private readonly HashSet<int> _hooked = new HashSet<int>();

    private Selectable[] _buffer = new Selectable[64];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameObject go = new GameObject(nameof(ButtonClickSfx));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<ButtonClickSfx>();
    }

    private void LateUpdate()
    {
        int count = Selectable.allSelectableCount;
        if (_buffer.Length < count) _buffer = new Selectable[Mathf.NextPowerOfTwo(count)];

        int filled = Selectable.AllSelectablesNoAlloc(_buffer);
        for (int i = 0; i < filled; i++)
        {
            Button button = _buffer[i] as Button;
            _buffer[i] = null;

            if (button == null) continue;
            if (button.gameObject.scene.name != GardenSceneName) continue;
            if (!_hooked.Add(button.GetInstanceID())) continue;

            button.onClick.AddListener(PlayClick);
        }
    }

    private static void PlayClick() => GameSfx.Play(SoundEffect.ButtonClick);
}
