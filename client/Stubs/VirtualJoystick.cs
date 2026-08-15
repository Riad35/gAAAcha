using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Dynamic virtual joystick: spawn at press origin in left screen zone (mouse/touch).
/// </summary>
public sealed class VirtualJoystick
{
    private readonly Rect _zone;
    private readonly float _radiusPx;
    private bool _active;
    private Vector2 _origin;
    private Vector2 _stick;
    private int _pointerId = -1;

    public VirtualJoystick(Rect zone, float radiusPx = 70f)
    {
        _zone = zone;
        _radiusPx = radiusPx;
    }

    public Vector2 Axis { get; private set; }

    public void Tick()
    {
        var mouse = Mouse.current;
        var touch = Touchscreen.current;

        if (!_active)
        {
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var p = mouse.position.ReadValue();
                if (_zone.Contains(p))
                {
                    Begin(p, -2);
                }
            }

            if (touch != null)
            {
                foreach (var t in touch.touches)
                {
                    if (!t.press.wasPressedThisFrame)
                    {
                        continue;
                    }

                    var p = t.position.ReadValue();
                    if (_zone.Contains(p))
                    {
                        Begin(p, t.touchId.ReadValue());
                        break;
                    }
                }
            }
        }
        else
        {
            Vector2 pos = _origin;
            var released = false;
            if (_pointerId == -2 && mouse != null)
            {
                pos = mouse.position.ReadValue();
                released = mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed;
            }
            else if (touch != null)
            {
                var found = false;
                foreach (var t in touch.touches)
                {
                    if (t.touchId.ReadValue() != _pointerId)
                    {
                        continue;
                    }

                    found = true;
                    pos = t.position.ReadValue();
                    released = t.press.wasReleasedThisFrame || !t.press.isPressed;
                    break;
                }

                if (!found)
                {
                    released = true;
                }
            }

            if (released)
            {
                End();
            }
            else
            {
                UpdateStick(pos);
            }
        }
    }

    public void Draw()
    {
        if (!_active)
        {
            return;
        }

        var baseRect = new Rect(_origin.x - _radiusPx, Screen.height - _origin.y - _radiusPx, _radiusPx * 2f, _radiusPx * 2f);
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(baseRect, Texture2D.whiteTexture);
        var knob = new Rect(_stick.x - 18f, Screen.height - _stick.y - 18f, 36f, 36f);
        GUI.color = new Color(0.4f, 0.9f, 1f, 0.7f);
        GUI.DrawTexture(knob, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void Begin(Vector2 screenPos, int id)
    {
        _active = true;
        _pointerId = id;
        _origin = screenPos;
        _stick = screenPos;
        Axis = Vector2.zero;
    }

    private void UpdateStick(Vector2 screenPos)
    {
        var delta = screenPos - _origin;
        if (delta.magnitude > _radiusPx)
        {
            delta = delta.normalized * _radiusPx;
        }

        _stick = _origin + delta;
        Axis = delta / _radiusPx;
    }

    private void End()
    {
        _active = false;
        _pointerId = -1;
        Axis = Vector2.zero;
    }
}
