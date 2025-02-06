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
using System.IO;
using System.Runtime.InteropServices;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Default
{
	sealed class Sdl3HardwareCursor : IHardwareCursor
	{
		public IntPtr Cursor { get; private set; }
		IntPtr surface;

		public Sdl3HardwareCursor(Size size, byte[] data, int2 hotspot)
		{
			try
			{
				var pixelFormat = SDL.SDL_GetPixelFormatForMasks(32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000);
				unsafe
				{
					surface = (IntPtr)SDL.SDL_CreateSurface(size.Width, size.Height, pixelFormat);
				}

				if (surface == IntPtr.Zero)
					throw new InvalidDataException($"Failed to create surface: {SDL.SDL_GetError()}");

				var sur = Marshal.PtrToStructure<SDL.SDL_Surface>(surface);
				Marshal.Copy(data, 0, sur.pixels, data.Length);

				// This call very occasionally fails on Windows, but often works when retried.
				for (var retries = 0; retries < 3 && Cursor == IntPtr.Zero; retries++)
					Cursor = SDL.SDL_CreateColorCursor(surface, hotspot.X, hotspot.Y);
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void Dispose(bool _)
		{
			if (Cursor != IntPtr.Zero)
			{
				SDL.SDL_DestroyCursor(Cursor);
				Cursor = IntPtr.Zero;
			}

			if (surface != IntPtr.Zero)
			{
				SDL.SDL_DestroySurface(surface);
				surface = IntPtr.Zero;
			}
		}

		~Sdl3HardwareCursor()
		{
			Dispose(false);
		}
	}
}
