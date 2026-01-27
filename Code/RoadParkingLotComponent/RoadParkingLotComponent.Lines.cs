using System;
using Sandbox;

namespace RedSnail.RoadTool;

[Flags]
public enum LineCap
{
	[Hide]
	None = 0,
	Start = 1 << 0,
	End = 1 << 1
}

public partial class RoadParkingLotComponent
{
	/// <summary>
	/// The material used to render the line segments between each parking spots
	/// </summary>
	[Property(Title = "Material"), Feature("Lines", Icon = "show_chart", Tint = EditorTint.Yellow)] private Material LinesMaterial { get; set { field = value; m_MeshBuilder?.IsDirty = true; } }

	/// <summary>
	/// Do we want line caps at the start and end of the parking lot ?
	/// </summary>
	[Property(Title = "Caps"), Feature("Lines")] private LineCap LinesCap { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = LineCap.Start | LineCap.End;

	/// <summary>
	/// The width of a line segment.
	/// </summary>
	[Property(Title = "Width"), Feature("Lines"), Range(1.0f, 50.0f)] private float LinesWidth { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 5.0f;

	/// <summary>
	/// The height offset above the ground (This is used to avoid Z fighting with the ground)
	/// </summary>
	[Property(Title = "Offset"), Feature("Lines"), Range(0.01f, 1.0f)] private float LinesOffset { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 0.1f;

	/// <summary>
	/// How many units before the line texture repeat itself (This is used to avoid stretching)
	/// </summary>
	[Property(Title = "Texture Repeat"), Feature("Lines")] private float LinesTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); m_MeshBuilder?.IsDirty = true; } } = 10.0f;



	private void BuildParkingLines()
	{
		int totalQuads = SpotCount + 1;

		m_MeshBuilder.InitSubmesh
		(
			"parking_lines",
			totalQuads * 4,
			totalQuads * 6,
			LinesMaterial ?? Material.Load("materials/dev/reflectivity_90.vmat"),
			_HasCollision: false
		);

		for (int i = 0; i <= SpotCount; i++)
		{
			if (i == 0 && !LinesCap.HasFlag(LineCap.Start))
				continue;

			if (i == SpotCount && !LinesCap.HasFlag(LineCap.End))
				continue;

			float spacing = CalculateSpacing();
			float xPos = i * spacing;

			DrawLine(xPos);
		}
	}



	private void DrawLine(float _PositionX)
	{
		Vector3 up = Vector3.Up;
		Vector3 forward = Vector3.Forward;

		float hw = LinesWidth * 0.5f;
		float angleRad = SpotAngle.DegreeToRadian();
		float sinAngle = float.Sin(angleRad);
		float cosAngle = float.Cos(angleRad);

		Vector3 lineDir = new Vector3(-sinAngle, cosAngle, 0);
		Vector3 perpDir = new Vector3(cosAngle, sinAngle, 0);

		Vector3 basePos = new Vector3(_PositionX, 0, 0);

		Vector3 p0 = basePos - perpDir * hw;
		Vector3 p1 = basePos + perpDir * hw;
		Vector3 p2 = p1 + lineDir * SpotLength;
		Vector3 p3 = p0 + lineDir * SpotLength;

		Vector3 t0 = p0 + up * LinesOffset;
		Vector3 t1 = p1 + up * LinesOffset;
		Vector3 t2 = p2 + up * LinesOffset;
		Vector3 t3 = p3 + up * LinesOffset;

		float v0 = SpotLength / LinesTextureRepeat;

		m_MeshBuilder.AddQuad
		(
			"parking_lines",
			t1, t2, t3, t0,
			up, forward,
			new Vector2(1, 0), new Vector2(1, v0), new Vector2(0, v0), new Vector2(0, 0)
		);
	}
}
