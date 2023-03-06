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
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	// Concept taken from https://www.redblobgames.com/grids/hexagons/implementation.html
	public class HoneyCombColorPickerWidget : Widget
	{
		readonly Ruleset modRules;

		public string ClickSound = ChromeMetrics.Get<string>("ClickSound");

		public event Action OnChange = () => { };

		public float H { get; private set; }
		public float S { get; private set; }
		public float V { get; private set; }

		bool isMoving;

		Vertex[] vertices;

		[ObjectCreator.UseCtor]
		public HoneyCombColorPickerWidget(ModData modData)
		{
			modRules = modData.DefaultRules;
			V = 1.0f;
		}

		public HoneyCombColorPickerWidget(HoneyCombColorPickerWidget other)
			: base(other)
		{
			modRules = other.modRules;
			ClickSound = other.ClickSound;
			OnChange = other.OnChange;
			H = other.H;
			S = other.S;
			V = other.V;
		}

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
		}

		int steps;
		float2 cellRadius;
		int2 hexagonCentre;
		int2 cursorPosition = int2.Zero;
		public override void Draw()
		{
			if (vertices == null)
			{
				steps = 6;
				cellRadius = new float2(Bounds.Width / (Sqrt3 * (steps * 2 + 1)), Bounds.Height / (steps * 3 + 1 + steps % 2));
				hexagonCentre = new int2(RenderOrigin.X + Bounds.Width / 2,  RenderOrigin.Y + Bounds.Height / 2);
				vertices = GenerateHexHexagonalGrid(hexagonCentre, steps, cellRadius);
			}

			Game.Renderer.RgbaColorRenderer.DrawVertices(vertices);

			Game.Renderer.RgbaColorRenderer.DrawPolygon(HexVerticesWithOffset(hexagonCentre, cellRadius, cursorPosition), 2f, Color.White);
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button != MouseButton.Left)
				return false;
			if (mi.Event == MouseInputEvent.Down && !TakeMouseFocus(mi))
				return false;
			if (!HasMouseFocus)
				return false;

			switch (mi.Event)
			{
				case MouseInputEvent.Up:
					isMoving = false;
					YieldMouseFocus(mi);
					break;

				case MouseInputEvent.Down:
					isMoving = true;
					if (SetValueFromPx(mi.Location))
					{
						OnChange();
						Game.Sound.PlayNotification(modRules, null, "Sounds", ClickSound, null);
					}

					break;

				case MouseInputEvent.Move:
					if (isMoving && SetValueFromPx(mi.Location))
						OnChange();

					break;
			}

			return true;
		}

		public Color Color => Color.FromAhsv(H, S, V);

		/// <summary>
		/// Set the color picker to nearest valid color to the given value.
		/// The saturation and brightness may be adjusted.
		/// </summary>
		public void Set(Color color)
		{
			var (_, h, s, _) = color.ToAhsv();

			if (H != h || S != s)
			{
				OnChange();
			}
		}

		Color[,] grid;

		int Hexes(int steps)
		{
			if (steps == 0)
				return 1;

			return steps * 6 + Hexes(steps - 1);
		}

		Vertex[] GenerateHexHexagonalGrid(int2 origin, int steps, float2 cellRadius)
		{
			Console.WriteLine(Hexes(steps));
			var vertices = new Vertex[12 * Hexes(steps)];
			var vertexCnt = 0;

			grid = new Color[steps * 2 + 1, steps * 2 + 1];
			for (var q = -steps; q <= steps; q++)
			{
				var r1 = Math.Max(-steps, -q - steps);
				var r2 = Math.Min(steps, -q + steps);
				for (var r = r1; r <= r2; r++)
				{
					// Exclude center
					if (q == 0 && r == 0)
						continue;

					var s = -q - r;

					var sum = (Math.Abs(q) + Math.Abs(r) + Math.Abs(s)) / 2;
					var step = 1f / steps;

					float cr, cg, cb;

					if (s > 0)
					{
						if (r < 0)
						{
							if (q > 0)
							{
								cr = (steps - s + 1) * step;
								cg = (steps - sum) * step;
								cb = (steps - sum) * step;
							}
							else if (q < 0)
							{
								cr = (steps + r + 1) * step;
								cg = (steps + r + 1) * step;
								cb = (steps - sum) * step;
							}
							else
							{
								cr = (steps + r) * step;
								cg = (steps + r + 1 + steps - sum) * step / 2;
								cb = (steps - sum) * step;
							}
						}
						else if (r > 0)
						{
							cr = (steps - sum) * step;
							cg = (steps - r + 1) * step;
							cb = (steps - sum) * step;
						}
						else
						{
							cr = (steps + steps - sum) * step / 2;
							cg = steps * step;
							cb = (steps - sum) * step;
						}
					}
					else if (s < 0)
					{
						if (r > 0)
						{
							if (q > 0)
							{
								cr = (steps - sum) * step;
								cg = (steps - sum) * step;
								cb = (steps - q + 1) * step;
							}
							else if (q < 0)
							{
								cr = (steps - sum) * step;
								cg = (steps + q + 1) * step;
								cb = (steps + q + 1) * step;
							}
							else
							{
								cr = (steps - sum) * step;
								cg = (steps + steps - sum) * step / 2;
								cb = steps * step;
							}
						}
						else if (r < 0)
						{
							cr = (steps + s + 1) * step;
							cg = (steps - sum) * step;
							cb = (steps + s + 1) * step;
						}
						else
						{
							cr = (steps + s + 1 + steps - sum) * step / 2;
							cg = (steps - sum) * step;
							cb = (steps + s) * step;
						}
					}
					else
					{
						if (r > 0)
						{
							cr = (steps + q + 1 + steps - sum) * step / 2;
							cg = (steps - sum) * step;
							cb = (steps + q + 1) * step;
						}
						else
						{
							cr = steps * step;
							cg = (steps + r) * step;
							cb = (steps + steps - sum) * step / 2;
						}
					}

					var col = Color.FromArgb((int)(cr * 255), (int)(cg * 255), (int)(cb * 255));

					grid[q + steps, r + steps] = col;

					foreach (var v in FillHex(HexVerticesWithOffset(origin, cellRadius, new float2(q, r)), col))
					{
						vertices[vertexCnt] = v;
						vertexCnt++;
					}
				}
			}

			return vertices;
		}

		static readonly float Sqrt3 = (float)Math.Sqrt(3);

		static readonly float2[] HexVertices =
		{
			new float2(0, 1f),
			new float2(Sqrt3 / 2, 1f / 2),
			new float2(Sqrt3 / 2, -1f / 2),
			new float2(0, -1f),
			new float2(-Sqrt3 / 2, -1f / 2),
			new float2(-Sqrt3 / 2, 1f / 2)
		};

		static float2[] HexVerticesWithOffset(int2 origin, float2 radius, float2 hex)
		{
			var corners = new float2[6];
			for (var i = 0; i < 6; i++)
				corners[i] = (HexToPx(hex) + HexVertices[i]) * radius + origin;

			return corners;
		}

		static float2 HexToPx(float2 hex) => new float2(Sqrt3 * (hex.X + hex.Y * 0.5f), 1.5f * hex.Y);

		public static int2 PxToHex(int2 xy, float2 radius)
		{
			var x = xy.X / radius.X;
			var y = xy.Y / radius.Y;

			return new int2((int)Math.Round((x * Sqrt3 + -y) / 3.0f), (int)Math.Round(y * 2.0f / 3.0f));
		}

		bool SetValueFromPx(int2 xy)
		{
			var hex = PxToHex(xy - hexagonCentre, cellRadius);
			Console.WriteLine(hex);

			if (Math.Abs(hex.X) <= steps && Math.Abs(hex.Y) <= steps && Math.Abs(hex.X) + Math.Abs(hex.Y) + Math.Abs(-hex.X - hex.Y) <= 2 * steps)
			{
				var color = grid[hex.X + steps, hex.Y + steps];
				if (color != Color.Black)
				{
					cursorPosition = hex;
					Console.WriteLine(color);
					(H, S, V) = Color.RgbToHsv(color.R, color.G, color.B);
					return true;
				}
			}

			return false;
		}

		static Vertex[] FillHex(float2[] hexVertices, Color color)
		{
			var cr = color.R / 255.0f;
			var cg = color.G / 255.0f;
			var cb = color.B / 255.0f;

			var offset = RgbaColorRenderer.Offset;

			// A hex is made up of 4 triangles.
			var vertices = new Vertex[12];
			for (var i = 0; i < 4; i++)
			{
				vertices[i * 3] = new Vertex(hexVertices[0] + offset, cr, cg, cb, 1f, 0, 0);
				vertices[1 + i * 3] = new Vertex(hexVertices[i + 1] + offset, cr, cg, cb, 1f, 0, 0);
				vertices[2 + i * 3] = new Vertex(hexVertices[i + 2] + offset, cr, cg, cb, 1f, 0, 0);
			}

			return vertices;
		}
	}
}
