#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Runtime.InteropServices;

namespace OpenRA.Platforms.Default
{
	sealed class Sdl3Input
	{
		MouseButton lastButtonBits = MouseButton.None;

		public static string GetClipboardText() { return SDL.SDL_GetClipboardText(); }
		public static bool SetClipboardText(string text) { return !SDL.SDL_SetClipboardText(text); }

		static MouseButton MakeButton(byte b)
		{
			switch (b)
			{
				case 1:
					return MouseButton.Left;
				case 2:
					return MouseButton.Middle;
				case 3:
					return MouseButton.Right;
				default: return MouseButton.None;
			}
		}

		static Modifiers MakeModifiers(int raw)
		{
			return ((raw & (int)SDL.SDL_Keymod.SDL_KMOD_ALT) != 0 ? Modifiers.Alt : 0)
				 | ((raw & (int)SDL.SDL_Keymod.SDL_KMOD_CTRL) != 0 ? Modifiers.Ctrl : 0)
				 | ((raw & (int)SDL.SDL_Keymod.SDL_KMOD_LGUI) != 0 ? Modifiers.Meta : 0)
				 | ((raw & (int)SDL.SDL_Keymod.SDL_KMOD_RGUI) != 0 ? Modifiers.Meta : 0)
				 | ((raw & (int)SDL.SDL_Keymod.SDL_KMOD_SHIFT) != 0 ? Modifiers.Shift : 0);
		}

		static int2 EventPosition(Sdl3PlatformWindow device, int x, int y)
		{
			// On Windows and Linux (X11) events are given in surface coordinates
			// These must be scaled to our effective window coordinates
			// Round fractional components up to avoid rounding small deltas to 0
			if (Platform.CurrentPlatform != PlatformType.OSX && device.EffectiveWindowSize != device.SurfaceSize)
			{
				var s = 1 / device.EffectiveWindowScale;
				return new int2((int)(Math.Sign(x) / 2f + x * s), (int)(Math.Sign(x) / 2f + y * s));
			}

			// On macOS we must still account for the user-requested scale modifier
			if (Platform.CurrentPlatform == PlatformType.OSX && device.EffectiveWindowScale != device.NativeWindowScale)
			{
				var s = device.NativeWindowScale / device.EffectiveWindowScale;
				return new int2((int)(Math.Sign(x) / 2f + x * s), (int)(Math.Sign(x) / 2f + y * s));
			}

			return new int2(x, y);
		}

		public void PumpInput(Sdl3PlatformWindow device, IInputHandler inputHandler, int2? lockedMousePosition)
		{
			var mods = MakeModifiers((int)SDL.SDL_GetModState());
			inputHandler.ModifierKeys(mods);
			MouseInput? pendingMotion = null;

			while (SDL.SDL_PollEvent(out var e))
			{
				switch ((SDL.SDL_EventType)e.type)
				{
					case SDL.SDL_EventType.SDL_EVENT_QUIT:
						// On macOS, we'd like to restrict Cmd + Q from suddenly exiting the game.
						if (Platform.CurrentPlatform != PlatformType.OSX || !mods.HasModifier(Modifiers.Meta))
							Game.Exit();

						break;

					case SDL.SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
						device.HasInputFocus = false;
						break;

					case SDL.SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
						device.HasInputFocus = true;
						break;

					// Triggered when moving between displays with different DPI settings
					case SDL.SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
					case SDL.SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
						device.WindowSizeChanged();
						break;

					case SDL.SDL_EventType.SDL_EVENT_WINDOW_HIDDEN:
					case SDL.SDL_EventType.SDL_EVENT_WINDOW_MINIMIZED:
						device.IsSuspended = true;
						break;

					case SDL.SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
					case SDL.SDL_EventType.SDL_EVENT_WINDOW_SHOWN:
					case SDL.SDL_EventType.SDL_EVENT_WINDOW_MAXIMIZED:
					case SDL.SDL_EventType.SDL_EVENT_WINDOW_RESTORED:
						device.IsSuspended = false;
						break;

					case SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
					case SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
					{
						// Mouse 1, Mouse 2 and Mouse 3 are handled as mouse inputs
						// Mouse 4 and Mouse 5 are treated as (pseudo) keyboard inputs
						if (e.button.button is 1 or 2 or 3)
						{
							if (pendingMotion != null)
							{
								inputHandler.OnMouseInput(pendingMotion.Value);
								pendingMotion = null;
							}

							var button = MakeButton(e.button.button);

							if (e.type == (uint)SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN)
								lastButtonBits |= button;
							else
								lastButtonBits &= ~button;

							var input = lockedMousePosition ?? new int2((int)e.button.x, (int)e.button.y);
							var pos = EventPosition(device, input.X, input.Y);

							if (e.type == (uint)SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN)
								inputHandler.OnMouseInput(new MouseInput(
									MouseInputEvent.Down, button, pos, int2.Zero, mods,
									MultiTapDetection.DetectFromMouse(e.button.button, pos)));
							else
								inputHandler.OnMouseInput(new MouseInput(
									MouseInputEvent.Up, button, pos, int2.Zero, mods,
									MultiTapDetection.InfoFromMouse(e.button.button)));
						}

						if (e.button.button is 4 or 5)
						{
							Keycode keyCode;

							if (e.button.button == (byte)SDL.SDL_MouseButtonFlags.SDL_BUTTON_X1MASK)
								keyCode = Keycode.MOUSE4;
							else
								keyCode = Keycode.MOUSE5;

							var type = e.type == (uint)SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN ?
								KeyInputEvent.Down : KeyInputEvent.Up;

							var tapCount = e.type == (uint)SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN ?
								MultiTapDetection.DetectFromKeyboard(keyCode, mods) :
								MultiTapDetection.InfoFromKeyboard(keyCode, mods);

							var keyEvent = new KeyInput
							{
								Event = type,
								Key = keyCode,
								Modifiers = mods,
								UnicodeChar = '?',
								MultiTapCount = tapCount,
								IsRepeat = e.key.repeat
							};
							inputHandler.OnKeyInput(keyEvent);
						}

						break;
					}

					case SDL.SDL_EventType.SDL_EVENT_MOUSE_MOTION:
					{
						var mousePos = new int2((int)e.motion.x, (int)e.motion.y);
						var input = lockedMousePosition ?? mousePos;
						var pos = EventPosition(device, input.X, input.Y);

						var delta = lockedMousePosition == null
							? EventPosition(device, (int)e.motion.xrel, (int)e.motion.yrel)
							: mousePos - lockedMousePosition.Value;

						pendingMotion = new MouseInput(
							MouseInputEvent.Move, lastButtonBits, pos, delta, mods, 0);

						break;
					}

					case SDL.SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
					{
						SDL.SDL_GetMouseState(out var x, out var y);

						var pos = EventPosition(device, (int)x, (int)y);
						inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Scroll, MouseButton.None, pos, new int2(0, (int)e.wheel.y), mods, 0));

						break;
					}

					case SDL.SDL_EventType.SDL_EVENT_TEXT_INPUT:
					{
						unsafe
						{
							var text = Marshal.PtrToStringUTF8((IntPtr)e.text.text);
							inputHandler.OnTextInput(text);
						}

						break;
					}

					case SDL.SDL_EventType.SDL_EVENT_KEY_DOWN:
					case SDL.SDL_EventType.SDL_EVENT_KEY_UP:
					{
						var keyCode = (Keycode)e.key.key;
						var type = e.type == (uint)SDL.SDL_EventType.SDL_EVENT_KEY_DOWN ?
							KeyInputEvent.Down : KeyInputEvent.Up;

						var tapCount = e.type == (uint)SDL.SDL_EventType.SDL_EVENT_KEY_DOWN ?
							MultiTapDetection.DetectFromKeyboard(keyCode, mods) :
							MultiTapDetection.InfoFromKeyboard(keyCode, mods);

						var keyEvent = new KeyInput
						{
							Event = type,
							Key = keyCode,
							Modifiers = mods,
							UnicodeChar = (char)e.key.key,
							MultiTapCount = tapCount,
							IsRepeat = e.key.repeat
						};

						// Special case workaround for windows users
						if (e.key.key == (uint)SDL.SDL_Keycode.SDLK_F4 && mods.HasModifier(Modifiers.Alt) &&
							Platform.CurrentPlatform == PlatformType.Windows)
							Game.Exit();
						else
							inputHandler.OnKeyInput(keyEvent);

						break;
					}
				}
			}

			if (pendingMotion != null)
				inputHandler.OnMouseInput(pendingMotion.Value);
		}
	}
}
