#if BROWSER
using System.Numerics;

namespace Foster.Framework;

internal sealed class InputProviderBrowser : InputProvider
{
	private const int MaxBrowserGamepads = 16;
	private const int GamepadButtonCount = 15;
	private const int GamepadAxisCount = 6;
	private const float AxisEpsilon = 0.0001f;

	private Vector2 lastMouse;
	private readonly bool[] lastMouseButtons = new bool[MouseState.MaxButtons];
	private readonly Dictionary<Keys, string> keyCodes = new()
	{
		[Keys.A] = "KeyA",
		[Keys.B] = "KeyB",
		[Keys.C] = "KeyC",
		[Keys.D] = "KeyD",
		[Keys.E] = "KeyE",
		[Keys.F] = "KeyF",
		[Keys.G] = "KeyG",
		[Keys.H] = "KeyH",
		[Keys.I] = "KeyI",
		[Keys.J] = "KeyJ",
		[Keys.K] = "KeyK",
		[Keys.L] = "KeyL",
		[Keys.M] = "KeyM",
		[Keys.N] = "KeyN",
		[Keys.O] = "KeyO",
		[Keys.P] = "KeyP",
		[Keys.Q] = "KeyQ",
		[Keys.R] = "KeyR",
		[Keys.S] = "KeyS",
		[Keys.T] = "KeyT",
		[Keys.U] = "KeyU",
		[Keys.V] = "KeyV",
		[Keys.W] = "KeyW",
		[Keys.X] = "KeyX",
		[Keys.Y] = "KeyY",
		[Keys.Z] = "KeyZ",
		[Keys.D1] = "Digit1",
		[Keys.D2] = "Digit2",
		[Keys.D3] = "Digit3",
		[Keys.D4] = "Digit4",
		[Keys.D5] = "Digit5",
		[Keys.D6] = "Digit6",
		[Keys.D7] = "Digit7",
		[Keys.D8] = "Digit8",
		[Keys.D9] = "Digit9",
		[Keys.D0] = "Digit0",
		[Keys.Enter] = "Enter",
		[Keys.Escape] = "Escape",
		[Keys.Backspace] = "Backspace",
		[Keys.Tab] = "Tab",
		[Keys.Space] = "Space",
		[Keys.Minus] = "Minus",
		[Keys.Equals] = "Equal",
		[Keys.LeftBracket] = "BracketLeft",
		[Keys.RightBracket] = "BracketRight",
		[Keys.Backslash] = "Backslash",
		[Keys.Semicolon] = "Semicolon",
		[Keys.Apostrophe] = "Quote",
		[Keys.Tilde] = "Backquote",
		[Keys.Comma] = "Comma",
		[Keys.Period] = "Period",
		[Keys.Slash] = "Slash",
		[Keys.Capslock] = "CapsLock",
		[Keys.F1] = "F1",
		[Keys.F2] = "F2",
		[Keys.F3] = "F3",
		[Keys.F4] = "F4",
		[Keys.F5] = "F5",
		[Keys.F6] = "F6",
		[Keys.F7] = "F7",
		[Keys.F8] = "F8",
		[Keys.F9] = "F9",
		[Keys.F10] = "F10",
		[Keys.F11] = "F11",
		[Keys.F12] = "F12",
		[Keys.F13] = "F13",
		[Keys.F14] = "F14",
		[Keys.F15] = "F15",
		[Keys.F16] = "F16",
		[Keys.F17] = "F17",
		[Keys.F18] = "F18",
		[Keys.F19] = "F19",
		[Keys.F20] = "F20",
		[Keys.F21] = "F21",
		[Keys.F22] = "F22",
		[Keys.F23] = "F23",
		[Keys.F24] = "F24",
		[Keys.PrintScreen] = "PrintScreen",
		[Keys.ScrollLock] = "ScrollLock",
		[Keys.Pause] = "Pause",
		[Keys.Insert] = "Insert",
		[Keys.Home] = "Home",
		[Keys.PageUp] = "PageUp",
		[Keys.Delete] = "Delete",
		[Keys.End] = "End",
		[Keys.PageDown] = "PageDown",
		[Keys.Right] = "ArrowRight",
		[Keys.Left] = "ArrowLeft",
		[Keys.Down] = "ArrowDown",
		[Keys.Up] = "ArrowUp",
		[Keys.Numlock] = "NumLock",
		[Keys.Application] = "ContextMenu",
		[Keys.KeypadDivide] = "NumpadDivide",
		[Keys.KeypadMultiply] = "NumpadMultiply",
		[Keys.KeypadMinus] = "NumpadSubtract",
		[Keys.KeypadPlus] = "NumpadAdd",
		[Keys.KeypadEnter] = "NumpadEnter",
		[Keys.Keypad1] = "Numpad1",
		[Keys.Keypad2] = "Numpad2",
		[Keys.Keypad3] = "Numpad3",
		[Keys.Keypad4] = "Numpad4",
		[Keys.Keypad5] = "Numpad5",
		[Keys.Keypad6] = "Numpad6",
		[Keys.Keypad7] = "Numpad7",
		[Keys.Keypad8] = "Numpad8",
		[Keys.Keypad9] = "Numpad9",
		[Keys.Keypad0] = "Numpad0",
		[Keys.KeypadPeroid] = "NumpadDecimal",
		[Keys.KeypadEquals] = "NumpadEqual",
		[Keys.KeypadComma] = "NumpadComma",
		[Keys.LeftControl] = "ControlLeft",
		[Keys.LeftShift] = "ShiftLeft",
		[Keys.LeftAlt] = "AltLeft",
		[Keys.LeftOS] = "MetaLeft",
		[Keys.RightControl] = "ControlRight",
		[Keys.RightShift] = "ShiftRight",
		[Keys.RightAlt] = "AltRight",
		[Keys.RightOS] = "MetaRight",
		[Keys.Mute] = "AudioVolumeMute",
		[Keys.VolumeUp] = "AudioVolumeUp",
		[Keys.VolumeDown] = "AudioVolumeDown",
	};
	private readonly Dictionary<Keys, bool> lastKeys = new();
	private readonly Dictionary<int, BrowserController> controllers = [];
	private uint nextControllerId = 1;

	private static readonly (int Source, Buttons Target)[] ButtonMap =
	[
		(0, Buttons.South),
		(1, Buttons.East),
		(2, Buttons.West),
		(3, Buttons.North),
		(4, Buttons.LeftShoulder),
		(5, Buttons.RightShoulder),
		(8, Buttons.Back),
		(9, Buttons.Start),
		(10, Buttons.LeftStick),
		(11, Buttons.RightStick),
		(12, Buttons.Up),
		(13, Buttons.Down),
		(14, Buttons.Left),
		(15, Buttons.Right),
		(16, Buttons.Guide),
	];

	private static readonly (int Source, Axes Target)[] AxisMap =
	[
		(0, Axes.LeftX),
		(1, Axes.LeftY),
		(2, Axes.RightX),
		(3, Axes.RightY),
	];

	public static Vector2 MousePosition
		=> new(BrowserWebGPU.GetMouseX(), BrowserWebGPU.GetMouseY());

	public InputProviderBrowser(App app) {}

	public override string GetClipboard()
		=> string.Empty;

	public override void SetClipboard(string text) {}

	public override void Rumble(ControllerID id, float lowIntensity, float highIntensity, float duration) {}

	public override void Update(in Time time)
	{
		var mouse = MousePosition;
		var delta = mouse - lastMouse;
		if (mouse != lastMouse)
		{
			MouseMove(mouse, delta, time.Elapsed);
			lastMouse = mouse;
		}

		for (var button = (int)MouseButtons.Left; button <= (int)MouseButtons.Right; button++)
		{
			var down = BrowserWebGPU.GetMouseButtonDown(button);
			if (down != lastMouseButtons[button])
			{
				MouseButton(button, down, time.Elapsed);
				lastMouseButtons[button] = down;
			}
		}

		var wheel = new Vector2(BrowserWebGPU.GetMouseWheelX(), BrowserWebGPU.GetMouseWheelY());
		if (wheel != Vector2.Zero)
		{
			MouseWheel(wheel);
			BrowserWebGPU.ResetMouseWheel();
		}

		foreach (var (key, code) in keyCodes)
		{
			var down = BrowserWebGPU.IsKeyDown(code);
			if (!lastKeys.TryGetValue(key, out var wasDown) || down != wasDown)
			{
				Key((int)key, down, time.Elapsed);
				lastKeys[key] = down;
			}
		}

		UpdateGamepads(time);
		base.Update(time);
	}

	private void UpdateGamepads(in Time time)
	{
		var slots = Math.Min(BrowserWebGPU.GetGamepadSlotCount(), MaxBrowserGamepads);
		for (var slot = 0; slot < slots; slot++)
		{
			if (!BrowserWebGPU.GetGamepadConnected(slot))
			{
				DisconnectGamepad(slot);
				continue;
			}

			if (!controllers.TryGetValue(slot, out var controller))
				controller = ConnectGamepad(slot);

			UpdateGamepadButtons(slot, controller, time.Elapsed);
			UpdateGamepadAxes(slot, controller, time.Elapsed);
		}

		foreach (var slot in controllers.Keys.ToArray())
			if (slot >= slots || !BrowserWebGPU.GetGamepadConnected(slot))
				DisconnectGamepad(slot);
	}

	private BrowserController ConnectGamepad(int slot)
	{
		var name = BrowserWebGPU.GetGamepadId(slot);
		if (string.IsNullOrWhiteSpace(name))
			name = $"Gamepad {slot}";

		var controller = new BrowserController(new(nextControllerId++));
		controllers[slot] = controller;

		ConnectController(
			id: controller.Id,
			name: name,
			buttonCount: GamepadButtonCount,
			axisCount: GamepadAxisCount,
			isGamepad: true,
			type: GuessGamepadType(name, BrowserWebGPU.GetGamepadMapping(slot)),
			vendor: 0,
			product: 0,
			version: 0);

		return controller;
	}

	private void DisconnectGamepad(int slot)
	{
		if (!controllers.Remove(slot, out var controller))
			return;

		DisconnectController(controller.Id);
	}

	private void UpdateGamepadButtons(int slot, BrowserController controller, TimeSpan elapsed)
	{
		var count = BrowserWebGPU.GetGamepadButtonCount(slot);
		foreach (var (source, target) in ButtonMap)
		{
			if (source >= count)
				continue;

			var button = (int)target;
			var down = BrowserWebGPU.GetGamepadButtonPressed(slot, source);
			if (controller.Buttons[button] == down)
				continue;

			controller.Buttons[button] = down;
			ControllerButton(controller.Id, button, down, elapsed);
		}
	}

	private void UpdateGamepadAxes(int slot, BrowserController controller, TimeSpan elapsed)
	{
		var axisCount = BrowserWebGPU.GetGamepadAxisCount(slot);
		foreach (var (source, target) in AxisMap)
		{
			if (source >= axisCount)
				continue;

			UpdateGamepadAxis(controller, target, Math.Clamp(BrowserWebGPU.GetGamepadAxisValue(slot, source), -1, 1), elapsed);
		}

		var buttonCount = BrowserWebGPU.GetGamepadButtonCount(slot);
		if (buttonCount > 6)
			UpdateGamepadAxis(controller, Axes.LeftTrigger, Math.Clamp(BrowserWebGPU.GetGamepadButtonValue(slot, 6), 0, 1), elapsed);
		if (buttonCount > 7)
			UpdateGamepadAxis(controller, Axes.RightTrigger, Math.Clamp(BrowserWebGPU.GetGamepadButtonValue(slot, 7), 0, 1), elapsed);
	}

	private void UpdateGamepadAxis(BrowserController controller, Axes axis, float value, TimeSpan elapsed)
	{
		var index = (int)axis;
		if (MathF.Abs(controller.Axes[index] - value) <= AxisEpsilon)
			return;

		controller.Axes[index] = value;
		ControllerAxis(controller.Id, index, value, elapsed);
	}

	private static GamepadTypes GuessGamepadType(string name, string mapping)
	{
		var lower = name.ToLowerInvariant();
		if (lower.Contains("dualsense") || lower.Contains("ps5"))
			return GamepadTypes.PS5;
		if (lower.Contains("dualshock") || lower.Contains("ps4"))
			return GamepadTypes.PS4;
		if (lower.Contains("xbox 360"))
			return GamepadTypes.Xbox360;
		if (lower.Contains("xbox"))
			return GamepadTypes.XboxOne;
		if (lower.Contains("switch") || lower.Contains("pro controller"))
			return GamepadTypes.NintendoSwitchPro;
		if (mapping == "standard")
			return GamepadTypes.Standard;
		return GamepadTypes.Unknown;
	}

	private sealed class BrowserController(ControllerID id)
	{
		public readonly ControllerID Id = id;
		public readonly bool[] Buttons = new bool[ControllerState.MaxButtons];
		public readonly float[] Axes = new float[ControllerState.MaxAxis];
	}
}
#endif
